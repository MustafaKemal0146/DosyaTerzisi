using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace DosyaTerzisi.Core;

/// <summary>
/// Reads the properties of documents and web pages: <c>DOC.*</c> for Office files (docx, xlsx, pptx and the OpenDocument
/// formats) and PDF, <c>HTML.*</c> for web pages. The old binary Office formats (.doc, .xls, .ppt) are not read.
/// </summary>
internal static partial class DocumentTags
{
    public static readonly HashSet<string> OfficeExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "docx", "docm", "dotx", "dotm", "xlsx", "xlsm", "xltx", "xltm", "pptx", "pptm", "potx", "potm", "odt", "ods", "odp",
    };

    public static readonly HashSet<string> HtmlExtensions = new(StringComparer.OrdinalIgnoreCase) { "htm", "html", "xhtml" };

    public static bool Handles(string extension) =>
        OfficeExtensions.Contains(extension) || OleProperties.Extensions.Contains(extension) || HtmlExtensions.Contains(extension)
        || extension.Equals("pdf", StringComparison.OrdinalIgnoreCase);

    public static void Read(string path, string extension, Dictionary<string, TagValue> values)
    {
        if (OleProperties.Extensions.Contains(extension)) OleProperties.Read(path, values);
        else if (OfficeExtensions.Contains(extension)) ReadOffice(path, extension, values);
        else if (HtmlExtensions.Contains(extension)) ReadHtml(path, values);
        else ReadPdf(path, values);
    }

    // ---- Office --------------------------------------------------------------------------------------------------

    private static void ReadOffice(string path, string extension, Dictionary<string, TagValue> values)
    {
        using var zip = ZipFile.OpenRead(path);

        if (extension.StartsWith("od", StringComparison.OrdinalIgnoreCase))
        {
            if (Load(zip, "meta.xml") is { } meta) ReadOpenDocument(meta, values);
            return;
        }

        if (Load(zip, "docProps/core.xml") is { } core)
        {
            Text(values, "DOC.Title", Value(core, "title"));
            Text(values, "DOC.Author", Value(core, "creator"));
            Text(values, "DOC.Subject", Value(core, "subject"));
            Text(values, "DOC.Keywords", Value(core, "keywords"));
            Text(values, "DOC.Comments", Value(core, "description"));
            Text(values, "DOC.Category", Value(core, "category"));
            Text(values, "DOC.LastModifiedBy", Value(core, "lastModifiedBy"));
            Date(values, "DOC.Created", Value(core, "created"));
            Date(values, "DOC.Modified", Value(core, "modified"));
        }

        if (Load(zip, "docProps/app.xml") is { } app)
        {
            Text(values, "DOC.Company", Value(app, "Company"));
            Number(values, "DOC.Pages", Value(app, "Pages"));
            Number(values, "DOC.Words", Value(app, "Words"));
        }
    }

    private static void ReadOpenDocument(XDocument meta, Dictionary<string, TagValue> values)
    {
        Text(values, "DOC.Title", Value(meta, "title"));
        Text(values, "DOC.Author", Value(meta, "initial-creator") ?? Value(meta, "creator"));
        Text(values, "DOC.Subject", Value(meta, "subject"));
        Text(values, "DOC.Keywords", string.Join(", ", meta.Descendants().Where(e => e.Name.LocalName == "keyword").Select(e => e.Value.Trim())));
        Text(values, "DOC.Comments", Value(meta, "description"));
        Text(values, "DOC.LastModifiedBy", Value(meta, "creator"));
        Date(values, "DOC.Created", Value(meta, "creation-date"));
        Date(values, "DOC.Modified", Value(meta, "date"));
    }

    private static XDocument? Load(ZipArchive zip, string name)
    {
        var entry = zip.GetEntry(name);
        if (entry is null || entry.Length > 2_000_000) return null;

        using var stream = entry.Open();
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        return XDocument.Load(reader);
    }

    private static string? Value(XDocument document, string localName) =>
        document.Descendants().FirstOrDefault(e => e.Name.LocalName == localName)?.Value;

    // ---- HTML ----------------------------------------------------------------------------------------------------

    private static void ReadHtml(string path, Dictionary<string, TagValue> values)
    {
        byte[] bytes;
        using (var stream = File.OpenRead(path))
        {
            bytes = new byte[Math.Min(stream.Length, 128 * 1024)];
            stream.ReadExactly(bytes);
        }

        string html = Decode(bytes);

        var title = TitlePattern().Match(html);
        if (title.Success) Text(values, "HTML.Title", Clean(title.Groups[1].Value));

        foreach (Match meta in MetaPattern().Matches(html))
        {
            string tag = meta.Value;
            string? name = Attribute(tag, "name");
            string? content = Attribute(tag, "content");
            if (name is null || content is null) continue;

            switch (name.ToLowerInvariant())
            {
                case "description": Text(values, "HTML.Description", Clean(content)); break;
                case "keywords": Text(values, "HTML.Keywords", Clean(content)); break;
                case "author": Text(values, "HTML.Author", Clean(content)); break;
                case "generator": Text(values, "HTML.Generator", Clean(content)); break;
            }
        }
    }

    /// <summary>Byte order mark first, then the charset the page announces, then UTF-8.</summary>
    private static string Decode(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE) return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF) return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);

        string ascii = Encoding.Latin1.GetString(bytes);
        var charset = CharsetPattern().Match(ascii);
        if (charset.Success)
        {
            try
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                return Encoding.GetEncoding(charset.Groups[1].Value).GetString(bytes);
            }
            catch (ArgumentException)
            {
                // Unknown charset name: fall through to UTF-8.
            }
        }

        return Encoding.UTF8.GetString(bytes);
    }

    private static string? Attribute(string tag, string name)
    {
        var match = Regex.Match(tag, $"""\b{name}\s*=\s*(?:"([^"]*)"|'([^']*)'|([^\s>]+))""", RegexOptions.IgnoreCase);
        if (!match.Success) return null;
        return match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Success ? match.Groups[2].Value : match.Groups[3].Value;
    }

    private static string Clean(string text) => WhitespacePattern().Replace(WebUtility.HtmlDecode(text), " ").Trim();

    [GeneratedRegex(@"<title[^>]*>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex TitlePattern();

    [GeneratedRegex(@"<meta\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex MetaPattern();

    [GeneratedRegex(@"charset\s*=\s*[""']?\s*([A-Za-z0-9_\-]+)", RegexOptions.IgnoreCase)]
    private static partial Regex CharsetPattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespacePattern();

    // ---- PDF -----------------------------------------------------------------------------------------------------

    /// <summary>
    /// Reads the document information of a PDF: <c>Title</c>, <c>Author</c>... Files that keep it inside compressed
    /// object streams are not read; they simply have no <c>DOC.*</c> tags.
    /// </summary>
    private static void ReadPdf(string path, Dictionary<string, TagValue> values)
    {
        byte[] bytes;
        using (var stream = File.OpenRead(path))
        {
            long length = stream.Length;
            int take = (int)Math.Min(length, 8 * 1024 * 1024);
            stream.Seek(length - take, SeekOrigin.Begin);
            bytes = new byte[take];
            stream.ReadExactly(bytes);
        }

        string text = Encoding.Latin1.GetString(bytes);
        string? dictionary = null;

        // The /Info reference is in the trailer or, for cross-reference streams, in the xref stream dictionary.
        var info = InfoReferencePattern().Matches(text).LastOrDefault();
        if (info is not null)
        {
            string number = info.Groups[1].Value;
            string generation = info.Groups[2].Value;
            var obj = Regex.Matches(text, $@"(?<!\d){number}\s+{generation}\s+obj(.*?)endobj", RegexOptions.Singleline).LastOrDefault();
            dictionary = obj?.Groups[1].Value ?? (int.TryParse(number, out int target) ? FromObjectStreams(bytes, text, target) : null);
        }

        if (dictionary is not null)
        {
            Text(values, "DOC.Title", PdfString(dictionary, "Title"));
            Text(values, "DOC.Author", PdfString(dictionary, "Author"));
            Text(values, "DOC.Subject", PdfString(dictionary, "Subject"));
            Text(values, "DOC.Keywords", PdfString(dictionary, "Keywords"));
            Text(values, "DOC.Application", PdfString(dictionary, "Creator"));
            Text(values, "DOC.Producer", PdfString(dictionary, "Producer"));
            PdfDate(values, "DOC.Created", PdfString(dictionary, "CreationDate"));
            PdfDate(values, "DOC.Modified", PdfString(dictionary, "ModDate"));
        }

        ReadXmp(bytes, text, values);
    }

    /// <summary>Finds object <paramref name="target"/> inside the compressed object streams (/Type /ObjStm) and returns its text.</summary>
    private static string? FromObjectStreams(byte[] bytes, string text, int target)
    {
        int from = 0;
        while (true)
        {
            int marker = text.IndexOf("/ObjStm", from, StringComparison.Ordinal);
            if (marker < 0) return null;
            from = marker + 7;

            int objStart = text.LastIndexOf("obj", marker, StringComparison.Ordinal);
            int streamWord = text.IndexOf("stream", marker, StringComparison.Ordinal);
            if (objStart < 0 || streamWord < 0) continue;

            string header = text[objStart..streamWord];
            var count = Regex.Match(header, @"/N\s+(\d+)");
            var first = Regex.Match(header, @"/First\s+(\d+)");
            if (!count.Success || !first.Success) continue;

            int dataStart = streamWord + 6;
            if (dataStart < text.Length && text[dataStart] == '\r') dataStart++;
            if (dataStart < text.Length && text[dataStart] == '\n') dataStart++;
            int dataEnd = text.IndexOf("endstream", dataStart, StringComparison.Ordinal);
            if (dataEnd < 0) dataEnd = text.Length;

            string content;
            try
            {
                using var source = new MemoryStream(bytes, dataStart, dataEnd - dataStart);
                if (header.Contains("FlateDecode", StringComparison.Ordinal))
                {
                    using var inflate = new ZLibStream(source, CompressionMode.Decompress);
                    using var output = new MemoryStream();
                    var buffer = new byte[16 * 1024];
                    int read;
                    while ((read = inflate.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        output.Write(buffer, 0, read);
                        if (output.Length > 16 * 1024 * 1024) break;
                    }

                    content = Encoding.Latin1.GetString(output.ToArray());
                }
                else content = Encoding.Latin1.GetString(bytes, dataStart, dataEnd - dataStart);
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException or ArgumentException)
            {
                continue;
            }

            int n = int.Parse(count.Groups[1].Value);
            int firstOffset = int.Parse(first.Groups[1].Value);
            var numbers = Regex.Matches(content[..Math.Min(firstOffset, content.Length)], @"\d+");
            for (int i = 0; i + 1 < numbers.Count && i / 2 < n; i += 2)
            {
                if (!int.TryParse(numbers[i].Value, out int number) || number != target) continue;
                if (!int.TryParse(numbers[i + 1].Value, out int offset)) continue;

                int begin = firstOffset + offset;
                if (begin >= content.Length) continue;
                int end = i + 3 < numbers.Count && int.TryParse(numbers[i + 3].Value, out int next) ? firstOffset + next : content.Length;
                if (end <= begin || end > content.Length) end = content.Length;
                return content[begin..end];
            }
        }
    }

    // ---- XMP -----------------------------------------------------------------------------------------------------

    private static void ReadXmp(byte[] bytes, string text, Dictionary<string, TagValue> values)
    {
        int start = text.LastIndexOf("<x:xmpmeta", StringComparison.Ordinal);
        if (start < 0) start = text.LastIndexOf("<xmpmeta", StringComparison.Ordinal);
        if (start < 0) return;
        int end = text.IndexOf("</x:xmpmeta>", start, StringComparison.Ordinal);
        int endLength = 12;
        if (end < 0)
        {
            end = text.IndexOf("</xmpmeta>", start, StringComparison.Ordinal);
            endLength = 10;
        }
        if (end < 0) return;

        XDocument document;
        try
        {
            using var stream = new MemoryStream(bytes, start, end + endLength - start);
            using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            document = XDocument.Load(reader);
        }
        catch (XmlException)
        {
            return;
        }

        Text(values, "DOC.Title", Xmp(document, "title"));
        Text(values, "DOC.Author", Xmp(document, "creator"));
        Text(values, "DOC.Subject", Xmp(document, "description"));
        Text(values, "DOC.Keywords", Xmp(document, "Keywords") ?? Xmp(document, "subject"));
        Text(values, "DOC.Application", Xmp(document, "CreatorTool"));
        Text(values, "DOC.Producer", Xmp(document, "Producer"));
        Date(values, "DOC.Created", Xmp(document, "CreateDate"));
        Date(values, "DOC.Modified", Xmp(document, "ModifyDate"));
    }

    /// <summary>The value of an XMP property: its text, its list items joined, or the attribute of the description.</summary>
    private static string? Xmp(XDocument document, string localName)
    {
        var element = document.Descendants().FirstOrDefault(e => e.Name.LocalName == localName);
        if (element is not null)
        {
            var items = element.Descendants().Where(e => e.Name.LocalName == "li").Select(e => e.Value.Trim()).Where(v => v.Length > 0).ToList();
            return items.Count > 0 ? string.Join(", ", items) : element.Value;
        }

        return document.Descendants().SelectMany(e => e.Attributes()).FirstOrDefault(a => a.Name.LocalName == localName)?.Value;
    }

    [GeneratedRegex(@"/Info\s+(\d+)\s+(\d+)\s+R")]
    private static partial Regex InfoReferencePattern();

    private static string? PdfString(string dictionary, string key)
    {
        var match = Regex.Match(dictionary, $@"/{key}\s*([(<])");
        if (!match.Success) return null;

        int start = match.Groups[1].Index;
        var data = new List<byte>();

        if (dictionary[start] == '(')
        {
            int depth = 0;
            for (int i = start; i < dictionary.Length; i++)
            {
                char c = dictionary[i];
                if (c == '\\' && i + 1 < dictionary.Length)
                {
                    char next = dictionary[++i];
                    switch (next)
                    {
                        case 'n': data.Add(10); break;
                        case 'r': data.Add(13); break;
                        case 't': data.Add(9); break;
                        case 'b': data.Add(8); break;
                        case 'f': data.Add(12); break;
                        case >= '0' and <= '7':
                            int octal = next - '0';
                            for (int k = 0; k < 2 && i + 1 < dictionary.Length && dictionary[i + 1] is >= '0' and <= '7'; k++)
                                octal = octal * 8 + (dictionary[++i] - '0');
                            data.Add((byte)octal);
                            break;
                        case '\r' or '\n': break;
                        default: data.Add((byte)next); break;
                    }
                    continue;
                }
                if (c == '(' && depth++ == 0) continue;
                if (c == ')' && --depth == 0) break;
                data.Add((byte)c);
            }
        }
        else
        {
            int end = dictionary.IndexOf('>', start);
            if (end < 0) return null;
            string hex = new(dictionary[(start + 1)..end].Where(Uri.IsHexDigit).ToArray());
            for (int i = 0; i + 1 < hex.Length; i += 2) data.Add(Convert.ToByte(hex.Substring(i, 2), 16));
        }

        var raw = data.ToArray();
        if (raw.Length >= 2 && raw[0] == 0xFE && raw[1] == 0xFF) return Encoding.BigEndianUnicode.GetString(raw, 2, raw.Length - 2);
        if (raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF) return Encoding.UTF8.GetString(raw, 3, raw.Length - 3);
        return Encoding.Latin1.GetString(raw);
    }

    private static void PdfDate(Dictionary<string, TagValue> values, string name, string? text)
    {
        if (text is null) return;
        var match = Regex.Match(text, @"(\d{4})(\d{2})?(\d{2})?(\d{2})?(\d{2})?(\d{2})?");
        if (!match.Success) return;

        int Part(int group, int fallback) => match.Groups[group].Success ? int.Parse(match.Groups[group].Value) : fallback;
        try
        {
            var date = new DateTime(Part(1, 1970), Part(2, 1), Part(3, 1), Part(4, 0), Part(5, 0), Part(6, 0));
            values.TryAdd(name, new TagValue(date.ToString("yyyy-MM-dd HH:mm:ss"), Date: date));
        }
        catch (ArgumentOutOfRangeException)
        {
        }
    }

    // ---- Values --------------------------------------------------------------------------------------------------

    private static void Text(Dictionary<string, TagValue> values, string name, string? text)
    {
        if (!string.IsNullOrWhiteSpace(text)) values.TryAdd(name, new TagValue(text.Trim()));
    }

    private static void Number(Dictionary<string, TagValue> values, string name, string? text)
    {
        if (double.TryParse(text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out double number) && number > 0)
            values.TryAdd(name, new TagValue(text!.Trim(), Number: number));
    }

    private static void Date(Dictionary<string, TagValue> values, string name, string? text)
    {
        if (DateTime.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var utc))
        {
            var local = utc.ToLocalTime();
            values.TryAdd(name, new TagValue(local.ToString("yyyy-MM-dd HH:mm:ss"), Date: local));
        }
    }
}
