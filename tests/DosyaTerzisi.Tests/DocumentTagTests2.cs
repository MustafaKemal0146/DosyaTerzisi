using System.IO.Compression;
using System.Text;
using DosyaTerzisi.Core;

namespace DosyaTerzisi.Tests;

/// <summary>Old binary Office files (OLE2), PDF object streams and XMP, and Windows shell properties (PROP.*).</summary>
public class DocumentTagTests2
{
    private static TagReadResult Read(string path) => FileTagReader.WithShellProperties.Read(FileEntry.FromPath(path)!);

    // ---- A tiny OLE2 writer, test only ---------------------------------------------------------------------------

    private static byte[] Property(ushort type, Action<BinaryWriter> body)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(type);
        writer.Write((ushort)0);
        body(writer);
        while (stream.Length % 4 != 0) writer.Write((byte)0);
        writer.Flush();
        return stream.ToArray();
    }

    private static byte[] Narrow(string text, int codePage)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Property(0x1E, w =>
        {
            var bytes = Encoding.GetEncoding(codePage).GetBytes(text);
            w.Write(bytes.Length + 1);
            w.Write(bytes);
            w.Write((byte)0);
        });
    }

    private static byte[] Wide(string text) => Property(0x1F, w =>
    {
        w.Write(text.Length + 1);
        w.Write(Encoding.Unicode.GetBytes(text));
        w.Write((short)0);
    });

    private static byte[] Int(int value) => Property(3, w => w.Write(value));

    private static byte[] Short(ushort value) => Property(2, w => w.Write((int)value));

    private static byte[] FileTime(DateTime utc) => Property(0x40, w => w.Write(utc.ToFileTimeUtc()));

    private static byte[] PropertySet(params (uint Id, byte[] Value)[] properties)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((ushort)0xFFFE);
        writer.Write((ushort)0);
        writer.Write(0x00020006);
        writer.Write(new byte[16]);
        writer.Write(1); // one section
        writer.Write(new byte[16]); // FMTID
        writer.Write(48); // section offset

        int offset = 8 + properties.Length * 8;
        writer.Write(offset + properties.Sum(p => p.Value.Length));
        writer.Write(properties.Length);
        foreach (var (id, value) in properties)
        {
            writer.Write(id);
            writer.Write(offset);
            offset += value.Length;
        }

        foreach (var (_, value) in properties) writer.Write(value);
        writer.Flush();
        return stream.ToArray();
    }

    /// <summary>One FAT sector, a directory, a mini FAT and mini stream (the small stream) and one regular stream of 4096 bytes.</summary>
    private static byte[] CompoundFile(byte[] summary, byte[] documentSummary)
    {
        Array.Resize(ref summary, 4096);
        int miniSectors = (documentSummary.Length + 63) / 64;
        var mini = new byte[miniSectors * 64];
        documentSummary.CopyTo(mini, 0);

        var file = new byte[512 + 512 * 12];
        void U32(int at, uint value) => BitConverter.GetBytes(value).CopyTo(file, at);
        void U16(int at, ushort value) => BitConverter.GetBytes(value).CopyTo(file, at);

        new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 }.CopyTo(file, 0);
        U16(0x1E, 9);
        U16(0x20, 6);
        U32(0x2C, 1); // one FAT sector
        U32(0x30, 1); // directory at sector 1
        U32(0x38, 4096);
        U32(0x3C, 2); // mini FAT at sector 2
        U32(0x40, 1);
        U32(0x44, 0xFFFFFFFE);
        for (int i = 0; i < 109; i++) U32(0x4C + i * 4, i == 0 ? 0u : 0xFFFFFFFF);

        int sector(int n) => 512 + n * 512;

        // FAT: 0 = FAT, 1 = directory, 2 = mini FAT, 3 = mini stream, 4..11 = summary
        for (int i = 0; i < 128; i++) U32(sector(0) + i * 4, 0xFFFFFFFF);
        U32(sector(0), 0xFFFFFFFD);
        U32(sector(0) + 4, 0xFFFFFFFE);
        U32(sector(0) + 8, 0xFFFFFFFE);
        U32(sector(0) + 12, 0xFFFFFFFE);
        for (int i = 4; i < 11; i++) U32(sector(0) + i * 4, (uint)(i + 1));
        U32(sector(0) + 11 * 4, 0xFFFFFFFE);

        // Directory
        void Entry(int index, string name, byte type, uint start, uint size)
        {
            int at = sector(1) + index * 128;
            Encoding.Unicode.GetBytes(name).CopyTo(file, at);
            U16(at + 64, (ushort)((name.Length + 1) * 2));
            file[at + 66] = type;
            U32(at + 116, start);
            U32(at + 120, size);
        }

        Entry(0, "Root Entry", 5, 3, (uint)mini.Length);
        Entry(1, "\u0005SummaryInformation", 2, 4, 4096);
        Entry(2, "\u0005DocumentSummaryInformation", 2, 0, (uint)documentSummary.Length);

        // Mini FAT
        for (int i = 0; i < 128; i++) U32(sector(2) + i * 4, 0xFFFFFFFF);
        for (int i = 0; i < miniSectors; i++) U32(sector(2) + i * 4, i == miniSectors - 1 ? 0xFFFFFFFE : (uint)(i + 1));

        mini.CopyTo(file, sector(3));
        summary.CopyTo(file, sector(4));
        return file;
    }

    [Fact]
    public void BinaryDoc_ReadsSummaryAndDocumentSummary()
    {
        using var tmp = new TempFolder();
        string path = Path.Combine(tmp.Root, "eski.doc");
        var created = new DateTime(2026, 3, 5, 10, 20, 30, DateTimeKind.Utc);

        var summary = PropertySet(
            (1, Short(1254)),
            (2, Narrow("Yıllık Rapor", 1254)),
            (3, Narrow("Mali durum", 1254)),
            (4, Wide("Ayşe Yılmaz")),
            (5, Narrow("bütçe, 2026", 1254)),
            (6, Narrow("Not", 1254)),
            (8, Narrow("Ali Yılmaz", 1254)),
            (12, FileTime(created)),
            (13, FileTime(created.AddDays(1))),
            (14, Int(7)),
            (15, Int(1234)));
        var extra = PropertySet(
            (1, Short(1254)),
            (2, Narrow("Rapor", 1254)),
            (15, Narrow("Üniversite", 1254)));
        System.IO.File.WriteAllBytes(path, CompoundFile(summary, extra));

        var tags = Read(path);

        Assert.Null(tags.Error);
        Assert.Equal("Yıllık Rapor", tags.Values["DOC.Title"].Text);
        Assert.Equal("Mali durum", tags.Values["DOC.Subject"].Text);
        Assert.Equal("Ayşe Yılmaz", tags.Values["DOC.Author"].Text);
        Assert.Equal("bütçe, 2026", tags.Values["DOC.Keywords"].Text);
        Assert.Equal("Not", tags.Values["DOC.Comments"].Text);
        Assert.Equal("Ali Yılmaz", tags.Values["DOC.LastModifiedBy"].Text);
        Assert.Equal(created, tags.Values["DOC.Created"].Date!.Value.ToUniversalTime());
        Assert.Equal(created.AddDays(1), tags.Values["DOC.Modified"].Date!.Value.ToUniversalTime());
        Assert.Equal(7, tags.Values["DOC.Pages"].Number);
        Assert.Equal(1234, tags.Values["DOC.Words"].Number);
        Assert.Equal("Rapor", tags.Values["DOC.Category"].Text);
        Assert.Equal("Üniversite", tags.Values["DOC.Company"].Text);
    }

    [Theory]
    [InlineData("kirik.doc", 100)]
    [InlineData("kirik.xls", 2000)]
    [InlineData("kirik.ppt", 0)]
    public void BrokenBinaryOffice_GivesErrorAndNoValues(string name, int length)
    {
        using var tmp = new TempFolder();
        string path = Path.Combine(tmp.Root, name);
        System.IO.File.WriteAllBytes(path, Enumerable.Range(0, length).Select(i => (byte)(i * 7)).ToArray());

        var tags = Read(path);

        Assert.NotNull(tags.Error);
        Assert.Empty(tags.Values);
    }

    [Fact]
    public void BinaryDoc_WithSignatureButDamagedFat_GivesError()
    {
        using var tmp = new TempFolder();
        string path = Path.Combine(tmp.Root, "yarim.doc");
        var file = new byte[4096];
        new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 }.CopyTo(file, 0);
        file[0x1E] = 9;
        file[0x20] = 6;
        file[0x2C] = 200; // claims 200 FAT sectors in a file of 7 sectors
        System.IO.File.WriteAllBytes(path, file);

        var tags = Read(path);

        Assert.NotNull(tags.Error);
        Assert.Empty(tags.Values);
    }

    // ---- PDF -----------------------------------------------------------------------------------------------------

    private static byte[] Pdf(params object[] parts)
    {
        using var stream = new MemoryStream();
        foreach (var part in parts)
        {
            var bytes = part is byte[] raw ? raw : Encoding.Latin1.GetBytes((string)part);
            stream.Write(bytes);
        }

        return stream.ToArray();
    }

    private static byte[] Deflate(string text)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
            zlib.Write(Encoding.Latin1.GetBytes(text));
        return output.ToArray();
    }

    [Fact]
    public void Pdf_InfoInsideCompressedObjectStream_IsRead()
    {
        using var tmp = new TempFolder();
        string path = Path.Combine(tmp.Root, "sikisik.pdf");

        const string header = "2 0 ";
        const string info = "<</Title (Sikisik Baslik)/Author (Yazar \\(Bir\\))/Keywords (a, b)/CreationDate (D:20260102030405)>>";
        byte[] packed = Deflate(header + info);

        System.IO.File.WriteAllBytes(path, Pdf(
            "%PDF-1.5\n1 0 obj\n<</Type/Catalog>>\nendobj\n",
            $"4 0 obj\n<</Type/ObjStm/N 1/First {header.Length}/Filter/FlateDecode/Length {packed.Length}>>\nstream\n",
            packed,
            "\nendstream\nendobj\n",
            "5 0 obj\n<</Type/XRef/Size 6/Root 1 0 R/Info 2 0 R/W[1 2 1]/Length 0>>\nstream\n\nendstream\nendobj\n",
            "startxref\n0\n%%EOF\n"));

        var tags = Read(path);

        Assert.Null(tags.Error);
        Assert.Equal("Sikisik Baslik", tags.Values["DOC.Title"].Text);
        Assert.Equal("Yazar (Bir)", tags.Values["DOC.Author"].Text);
        Assert.Equal("a, b", tags.Values["DOC.Keywords"].Text);
        Assert.Equal(new DateTime(2026, 1, 2, 3, 4, 5), tags.Values["DOC.Created"].Date);
    }

    [Fact]
    public void Pdf_OnlyXmp_FillsTitleAuthorKeywordsAndCreated()
    {
        using var tmp = new TempFolder();
        string path = Path.Combine(tmp.Root, "xmp.pdf");

        const string xmp = """
            <?xpacket begin="" id="W5M0MpCehiHzreSzNTczkc9d"?>
            <x:xmpmeta xmlns:x="adobe:ns:meta/"><rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">
              <rdf:Description rdf:about="" xmlns:dc="http://purl.org/dc/elements/1.1/" xmlns:pdf="http://ns.adobe.com/pdf/1.3/" xmlns:xmp="http://ns.adobe.com/xap/1.0/">
                <dc:title><rdf:Alt><rdf:li xml:lang="x-default">Başlık Xmp</rdf:li></rdf:Alt></dc:title>
                <dc:creator><rdf:Seq><rdf:li>Ayşe</rdf:li></rdf:Seq></dc:creator>
                <pdf:Keywords>bir, iki</pdf:Keywords>
                <xmp:CreateDate>2026-02-03T04:05:06Z</xmp:CreateDate>
              </rdf:Description></rdf:RDF></x:xmpmeta>
            <?xpacket end="w"?>
            """;

        System.IO.File.WriteAllBytes(path, Pdf(
            "%PDF-1.4\n1 0 obj\n<</Type/Catalog/Metadata 2 0 R>>\nendobj\n2 0 obj\n<</Type/Metadata/Subtype/XML>>\nstream\n",
            Encoding.UTF8.GetBytes(xmp),
            "\nendstream\nendobj\ntrailer\n<</Root 1 0 R>>\n%%EOF\n"));

        var tags = Read(path);

        Assert.Null(tags.Error);
        Assert.Equal("Başlık Xmp", tags.Values["DOC.Title"].Text);
        Assert.Equal("Ayşe", tags.Values["DOC.Author"].Text);
        Assert.Equal("bir, iki", tags.Values["DOC.Keywords"].Text);
        Assert.NotNull(tags.Values["DOC.Created"].Date);
    }

    // ---- PROP.* --------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("Bit hızı", "Bithızı")]
    [InlineData("Dimensions", "Dimensions")]
    [InlineData("Sample-rate (Hz)", "Sample-rateHz")]
    [InlineData("", "")]
    public void PropertyColumnNames_AreCleaned(string name, string expected) => Assert.Equal(expected, PropertyTags.CleanName(name));

    [Fact]
    public void ShellProperties_DoNotThrowAndNeverReplaceOtherTags()
    {
        using var tmp = new TempFolder();
        string path = tmp.File("notlar.txt", "merhaba");

        var tags = Read(path);

        Assert.Null(tags.Error);
        Assert.All(tags.Values.Keys, key => Assert.StartsWith("PROP.", key));
        if (OperatingSystem.IsWindows()) Assert.NotEmpty(tags.Values); // the shell always knows at least the size and the type

        // A docx keeps its DOC.* values; shell properties are only added next to them.
        string docx = Path.Combine(tmp.Root, "a.docx");
        using (var archive = ZipFile.Open(docx, ZipArchiveMode.Create))
        {
            using var writer = new StreamWriter(archive.CreateEntry("docProps/core.xml").Open(), new UTF8Encoding(false));
            writer.Write("""<cp:coreProperties xmlns:cp="http://schemas.openxmlformats.org/package/2006/metadata/core-properties" xmlns:dc="http://purl.org/dc/elements/1.1/"><dc:title>Asıl Başlık</dc:title></cp:coreProperties>""");
        }

        var document = Read(docx);

        Assert.Null(document.Error);
        Assert.Equal("Asıl Başlık", document.Values["DOC.Title"].Text);
    }
}
