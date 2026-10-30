using System.Globalization;
using DosyaTerzisi.Core;
using static DosyaTerzisi.Tests.TestHelpers;

namespace DosyaTerzisi.Tests;

public class StrftimeAndShortNameTests
{
    // Stamp = 2026-07-01 15:33:07 (a Wednesday)
    private static string Expand(string template) =>
        ReplacementTemplate.Expand(template, new ExpansionContext(File("a.txt"), [], 1, DateSource.Modified, CultureInfo.InvariantCulture));

    // ---- strftime ---------------------------------------------------------------------------------------------

    [Fact]
    public void Percent_IsTheSameAsBackslash()
    {
        Assert.Equal(Expand(@"\Y\m\d"), Expand("%Y%m%d"));
        Assert.Equal("20260701", Expand("%Y%m%d"));
        Assert.Equal(Expand(@"\Y-\m-\d \H.\M.\S"), Expand("%Y-%m-%d %H.%M.%S"));
        Assert.Equal("2026-07-01 15.33.07", Expand("%Y-%m-%d %H.%M.%S"));
    }

    [Fact]
    public void Percent_CoversTheOtherDateCodes()
    {
        Assert.Equal("26 03 PM 182 3", Expand("%y %I %p %j %w"));
        Assert.Equal("Wed Wednesday Jul July", Expand("%a %A %b %B"));
    }

    [Fact]
    public void DoublePercent_WritesOnePercent()
    {
        Assert.Equal("100% 2026", Expand("100%% %Y"));
        Assert.Equal("%Y", Expand("%%Y"));
    }

    [Fact]
    public void PercentHash_DropsTheLeadingZero()
    {
        Assert.Equal("1", Expand("%#d"));
        Assert.Equal("7", Expand("%#m"));
        Assert.Equal(Expand(@"\#d"), Expand("%#d"));
    }

    [Fact]
    public void PercentWithUnknownLetter_StaysAsItIs()
    {
        Assert.Equal("%q 2026", Expand("%q %Y"));
        Assert.Equal("50%", Expand("50%"));
        Assert.Equal("%# x", Expand("%# x"));
    }

    [Fact]
    public void TagFormats_AreNotTouchedByPercent()
    {
        var tags = new Dictionary<string, TagValue> { ["EXIF.OriginalTime"] = Tv(new DateTime(2019, 8, 17, 14, 30, 5)) };
        var ctx = new ExpansionContext(File("a.jpg"), [], 1, DateSource.Modified, CultureInfo.InvariantCulture, tags);
        Assert.Equal("20190817_2026", ReplacementTemplate.Expand("<EXIF.OriginalTime:yyyyMMdd>_%Y", ctx));
    }

    private static TagValue Tv(DateTime date) => new(date.ToString("s"), Date: date);

    // ---- 8.3 conversion ---------------------------------------------------------------------------------------

    [Theory]
    [InlineData("kisa.txt", "kisa.txt")]
    [InlineData("cok uzun bir dosya adi.txt", "cok_uz~1.txt")]
    [InlineData("rapor.docx", "rapor.doc")]
    [InlineData("a b,c;d=e+f.txt", "a_b_c_~1.txt")]
    [InlineData("a b.c", "a_b.c")]
    [InlineData("özet çalışma raporu.pdf", "özet_ç~1.pdf")]
    [InlineData("yedek", "yedek")]
    [InlineData("dokuzharfli", "dokuzh~1")]
    [InlineData(".gitignore", "DOSYA.git")]
    public void Convert_ShapesTheName(string input, string expected)
    {
        Assert.Equal(expected, ShortName83.Convert(input, isDirectory: false));
    }

    [Fact]
    public void Convert_KeepsCase_AndTurkishLetters()
    {
        Assert.Equal("Ağaç.TXT", ShortName83.Convert("Ağaç.TXT", false));
    }

    [Fact]
    public void Convert_Folder_HasNoExtension()
    {
        Assert.Equal("my_fol~1", ShortName83.Convert("my folder.old", true));
        Assert.Equal("klasör", ShortName83.Convert("klasör", true));
    }

    [Fact]
    public void Convert_CounterIsUniqueWithinTheSet()
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Assert.Equal("rapor_~1.txt", ShortName83.Convert("rapor_yil_2024.txt", false, used));
        Assert.Equal("rapor_~2.txt", ShortName83.Convert("rapor_yil_2025.txt", false, used));
        Assert.Equal("RAPOR_~3.txt", ShortName83.Convert("RAPOR_yil_2026.txt", false, used));
        Assert.Equal("rapor_~1.doc", ShortName83.Convert("rapor_yil_2024.doc", false, used));
    }

    // ---- planner ----------------------------------------------------------------------------------------------

    private static RenameOptions Keep(bool shortNames = true) =>
        new() { Method = RenameMethod.Wildcard, Search = "*", Replace = @"\0", ShortNames83 = shortNames };

    [Fact]
    public void Planner_ShortensLongName()
    {
        var items = RenamePlanner.Build([File("cok uzun bir dosya adi.txt")], Keep(), NoDisk);
        Assert.Equal("cok_uz~1.txt", items[0].NewName);
        Assert.Equal(RenameStatus.Ready, items[0].Status);
    }

    [Fact]
    public void Planner_IsOffByDefault()
    {
        var items = RenamePlanner.Build([File("cok uzun bir dosya adi.txt")], Keep(shortNames: false), NoDisk);
        Assert.Equal(RenameStatus.Unchanged, items[0].Status);
        Assert.Equal("cok uzun bir dosya adi.txt", items[0].NewName);
    }

    [Fact]
    public void Planner_CounterIsUniqueAcrossFiles_InTheSameFolder()
    {
        var files = new[]
        {
            File("rapor genel 2024.txt"),
            File("rapor genel 2025.txt"),
            File("rapor genel 2026.txt"),
            File("rapor genel 2026.txt", @"C:\Baska"),
        };
        var items = RenamePlanner.Build(files, Keep(), NoDisk);
        Assert.Equal(["rapor_~1.txt", "rapor_~2.txt", "rapor_~3.txt", "rapor_~1.txt"], items.Select(i => i.NewName));
        Assert.All(items, i => Assert.Equal(RenameStatus.Ready, i.Status));
    }

    [Fact]
    public void Planner_Folder_GetsEightCharacters()
    {
        var items = RenamePlanner.Build([Folder("Belgeler ve Evraklar")], Keep(), NoDisk);
        Assert.Equal("Belgel~1", items[0].NewName);
    }

    [Fact]
    public void Planner_AlreadyCompliantName_StaysUnchanged()
    {
        var items = RenamePlanner.Build([File("kisa.txt"), Folder("klasor")], Keep(), NoDisk);
        Assert.All(items, i => Assert.Equal(RenameStatus.Unchanged, i.Status));
    }

    [Fact]
    public void Planner_WithTargetPath_OnlyTheLastPartIsShortened()
    {
        var o = new RenameOptions { Method = RenameMethod.Wildcard, Search = "*", Replace = "uzun klasor/\\0", ShortNames83 = true };
        var item = RenamePlanner.Build([File("cok uzun bir dosya adi.txt")], o, NoDisk)[0];
        Assert.Equal("cok_uz~1.txt", item.NewName);
        Assert.EndsWith("uzun klasor", item.DestinationDirectory);
    }

    [Fact]
    public void Planner_StillChecksConflictsAfterwards()
    {
        // "a b.txt" becomes a_b.txt, which already exists on disk: ResolveConflicts still reports it.
        var disk = new PlanOptions { ListDirectory = _ => ["a b.txt", "a_b.txt"] };
        var items = RenamePlanner.Build([File("a b.txt")], Keep(), disk);
        Assert.Equal("a_b.txt", items[0].NewName);
        Assert.Equal(RenameStatus.Conflict, items[0].Status);
    }

    // ---- tag rename -------------------------------------------------------------------------------------------

    [Fact]
    public void TagRename_ShortensToo()
    {
        var reader = new MediaFixtures.FakeTags(MediaFixtures.Text("ID3.Artist", "Queen"), MediaFixtures.Text("ID3.Track", "Bohemian Rhapsody"));
        var options = new TagRenameOptions { Template = @"<ID3.Artist> - <ID3.Track>.\e", ShortNames83 = true };
        var files = new[] { File("a.mp3"), File("b.mp3") };

        var items = TagRenamePlanner.Build(files, options, reader, NoDisk);

        Assert.Equal("Queen_~1.mp3", items[0].NewName);
        Assert.Equal("Queen_~2.mp3", items[1].NewName);
    }

    [Fact]
    public void TagRename_StrftimeInTemplate()
    {
        var reader = new MediaFixtures.FakeTags(MediaFixtures.Text("ID3.Artist", "Queen"));
        var options = new TagRenameOptions { Template = @"<ID3.Artist> %Y%m%d.\e" };
        Assert.Equal("Queen 20260701.mp3", TagRenamePlanner.Build([File("a.mp3")], options, reader, NoDisk)[0].NewName);
    }
}
