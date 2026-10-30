using System.IO.Compression;
using System.Text;
using DosyaTerzisi.Core;

namespace DosyaTerzisi.Tests;

/// <summary>DOC.* (Office, OpenDocument, PDF) and HTML.* tags, read from small real files.</summary>
public class DocumentTagTests
{
    private static TagReadResult Read(string path) => FileTagReader.Default.Read(FileEntry.FromPath(path)!);

    private static string Text(TagReadResult r, string name) => r.Values[name].Text;

    private static void Zip(string path, params (string Name, string Content)[] parts)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (name, content) in parts)
        {
            using var writer = new StreamWriter(archive.CreateEntry(name).Open(), new UTF8Encoding(false));
            writer.Write(content);
        }
    }

    [Fact]
    public void Docx_ReadsTitleAuthorAndDates()
    {
        using var tmp = new TempFolder();
        string path = Path.Combine(tmp.Root, "rapor.docx");
        Zip(path,
            ("docProps/core.xml", """
                <cp:coreProperties xmlns:cp="http://schemas.openxmlformats.org/package/2006/metadata/core-properties"
                    xmlns:dc="http://purl.org/dc/elements/1.1/" xmlns:dcterms="http://purl.org/dc/terms/">
                  <dc:title>Yıllık Rapor</dc:title><dc:creator>Ayşe Yılmaz</dc:creator>
                  <dc:subject>Mali durum</dc:subject><cp:keywords>2026, bütçe</cp:keywords>
                  <cp:lastModifiedBy>Ali Yılmaz</cp:lastModifiedBy>
                  <dcterms:created>2026-01-15T10:30:00Z</dcterms:created>
                </cp:coreProperties>
                """),
            ("docProps/app.xml", """<Properties xmlns="http://schemas.openxmlformats.org/officeDocument/2006/extended-properties"><Company>Üniversite</Company><Pages>12</Pages></Properties>"""));

        var tags = Read(path);

        Assert.Null(tags.Error);
        Assert.Equal("Yıllık Rapor", Text(tags, "DOC.Title"));
        Assert.Equal("Ayşe Yılmaz", Text(tags, "DOC.Author"));
        Assert.Equal("Mali durum", Text(tags, "DOC.Subject"));
        Assert.Equal("2026, bütçe", Text(tags, "DOC.Keywords"));
        Assert.Equal("Üniversite", Text(tags, "DOC.Company"));
        Assert.Equal(12, tags.Values["DOC.Pages"].Number);
        Assert.NotNull(tags.Values["DOC.Created"].Date);
    }

    [Fact]
    public void OpenDocument_ReadsMetaXml()
    {
        using var tmp = new TempFolder();
        string path = Path.Combine(tmp.Root, "ders.odt");
        Zip(path, ("meta.xml", """
            <office:document-meta xmlns:office="urn:oasis:names:tc:opendocument:xmlns:office:1.0"
                xmlns:dc="http://purl.org/dc/elements/1.1/" xmlns:meta="urn:oasis:names:tc:opendocument:xmlns:meta:1.0">
              <office:meta><dc:title>Ders Notu</dc:title><meta:initial-creator>Can</meta:initial-creator>
              <meta:keyword>fizik</meta:keyword><meta:keyword>ısı</meta:keyword></office:meta>
            </office:document-meta>
            """));

        var tags = Read(path);

        Assert.Equal("Ders Notu", Text(tags, "DOC.Title"));
        Assert.Equal("Can", Text(tags, "DOC.Author"));
        Assert.Equal("fizik, ısı", Text(tags, "DOC.Keywords"));
    }

    [Fact]
    public void ADamagedOfficeFile_GivesAnError_NotACrash()
    {
        using var tmp = new TempFolder();
        string path = tmp.File("bozuk.docx", "bu bir zip değil");

        var tags = Read(path);

        Assert.NotNull(tags.Error);
        Assert.Empty(tags.Values);
    }

    [Fact]
    public void Pdf_ReadsTheInfoDictionary_IncludingUtf16AndEscapes()
    {
        using var tmp = new TempFolder();
        string path = Path.Combine(tmp.Root, "makale.pdf");
        // Title as UTF-16BE hex string, Author as a literal string with an escaped parenthesis and octal letter.
        string titleHex = "FEFF" + string.Concat("Çalışma Planı".Select(c => ((int)c).ToString("X4")));
        string pdf = $"%PDF-1.4\n1 0 obj\n<< /Type /Catalog >>\nendobj\n7 0 obj\n<< /Title <{titleHex}> /Author (Ali \\(Veli\\) \\374) /CreationDate (D:20260115103000+03'00') >>\nendobj\ntrailer\n<< /Root 1 0 R /Info 7 0 R >>\n%%EOF\n";
        File.WriteAllBytes(path, Encoding.Latin1.GetBytes(pdf));

        var tags = Read(path);

        Assert.Equal("Çalışma Planı", Text(tags, "DOC.Title"));
        Assert.Equal("Ali (Veli) ü", Text(tags, "DOC.Author"));
        Assert.Equal(new DateTime(2026, 1, 15, 10, 30, 0), tags.Values["DOC.Created"].Date);
    }

    [Fact]
    public void APdfWithoutInfo_HasNoTags()
    {
        using var tmp = new TempFolder();
        string path = tmp.File("bos.pdf", "%PDF-1.7\n%%EOF");

        var tags = Read(path);

        Assert.Null(tags.Error);
        Assert.Empty(tags.Values.Keys.Where(k => !k.StartsWith("PROP.", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void Html_ReadsTitleAndMeta_InTheAnnouncedCharset()
    {
        using var tmp = new TempFolder();
        string path = Path.Combine(tmp.Root, "sayfa.html");
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var turkish = Encoding.GetEncoding(1254);
        File.WriteAllBytes(path, turkish.GetBytes(
            "<html><head><meta charset=\"windows-1254\"><title>\n  Öğrenci   Şeması &amp; Notlar </title>"
            + "<meta name=\"description\" content=\"Güz dönemi\"><meta content='Mustafa Kemal' name='author'></head></html>"));

        var tags = Read(path);

        Assert.Equal("Öğrenci Şeması & Notlar", Text(tags, "HTML.Title"));
        Assert.Equal("Güz dönemi", Text(tags, "HTML.Description"));
        Assert.Equal("Mustafa Kemal", Text(tags, "HTML.Author"));
    }

    [Fact]
    public void DocumentTags_WorkInATagRename()
    {
        using var tmp = new TempFolder();
        string path = Path.Combine(tmp.Root, "x.docx");
        Zip(path, ("docProps/core.xml", """<cp:coreProperties xmlns:cp="http://schemas.openxmlformats.org/package/2006/metadata/core-properties" xmlns:dc="http://purl.org/dc/elements/1.1/"><dc:title>Bütçe 2026</dc:title></cp:coreProperties>"""));

        var items = TagRenamePlanner.Build(
            FileScanner.Scan(tmp.Root, new ScanOptions()).Entries,
            new TagRenameOptions { Template = @"<DOC.Title>.\e" });

        Assert.Equal("Bütçe 2026.docx", items[0].NewName);
        Assert.Equal(RenameStatus.Ready, items[0].Status);
    }
}
