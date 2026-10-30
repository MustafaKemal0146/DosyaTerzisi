using DosyaTerzisi.Core;

namespace DosyaTerzisi.Tests;

/// <summary>"Numaralandırmayı yeniden başlat": klasöre, tarihe (gün) ve etikete göre, gerçek dosyalarla.</summary>
public class NumberRestartTests
{
    private static string Mp3(TempFolder tmp, string name, string album, DateTime modified)
    {
        string path = MediaFixtures.WriteMp3(Path.Combine(tmp.Root, name), t => t.Album = album);
        File.SetLastWriteTime(path, modified);
        return path;
    }

    private static List<FileEntry> Entries(params string[] paths) => paths.Select(p => FileEntry.FromPath(p)!).ToList();

    private static RenameOptions Rule() => new() { Method = RenameMethod.Wildcard, Search = "*.*", Replace = @"??-\1.\2" };

    private static string[] Names(IEnumerable<RenameItem> items) => items.Select(i => i.NewName).ToArray();

    [Fact]
    public void NoRestartOption_CountsEverythingInOneGroup()
    {
        using var tmp = new TempFolder();
        var d1 = new DateTime(2026, 3, 1, 10, 0, 0);
        var d2 = new DateTime(2026, 3, 2, 10, 0, 0);
        var entries = Entries(Mp3(tmp, "a.mp3", "X", d1), Mp3(tmp, "b.mp3", "Y", d2), Mp3(tmp, "c.mp3", "X", d1));

        var plan = RenamePlanner.Build(entries, Rule(), TestHelpers.NoDisk);

        Assert.Equal(["01-a.mp3", "02-b.mp3", "03-c.mp3"], Names(plan));
    }

    [Fact]
    public void PerDate_RestartsEveryDay()
    {
        using var tmp = new TempFolder();
        var d1 = new DateTime(2026, 3, 1, 8, 0, 0);
        var d1Late = new DateTime(2026, 3, 1, 23, 59, 0);
        var d2 = new DateTime(2026, 3, 2, 0, 1, 0);
        var entries = Entries(Mp3(tmp, "a.mp3", "X", d1), Mp3(tmp, "b.mp3", "X", d2), Mp3(tmp, "c.mp3", "X", d1Late));
        var options = Rule();
        options.ResetNumberPerDate = true;

        var plan = RenamePlanner.Build(entries, options, TestHelpers.NoDisk);

        // Saat farkı önemli değil, yalnızca gün: a ve c aynı gün.
        Assert.Equal(["01-a.mp3", "01-b.mp3", "02-c.mp3"], Names(plan));
    }

    [Fact]
    public void PerDate_FollowsDateSource()
    {
        var created1 = new DateTime(2026, 1, 1);
        var created2 = new DateTime(2026, 1, 2);
        var same = new DateTime(2026, 5, 5);
        var entries = new List<FileEntry>
        {
            new(@"C:\x", "a.txt", false, same, created1),
            new(@"C:\x", "b.txt", false, same, created2),
        };
        var options = Rule();
        options.ResetNumberPerDate = true;

        Assert.Equal(["01-a.txt", "02-b.txt"], Names(RenamePlanner.Build(entries, options, TestHelpers.NoDisk)));

        options.DateSource = DateSource.Created;
        Assert.Equal(["01-a.txt", "01-b.txt"], Names(RenamePlanner.Build(entries, options, TestHelpers.NoDisk)));
    }

    [Fact]
    public void PerTag_SameAlbumCountsTogether()
    {
        using var tmp = new TempFolder();
        var d = new DateTime(2026, 3, 1);
        var entries = Entries(
            Mp3(tmp, "a.mp3", "Alfa", d), Mp3(tmp, "b.mp3", "Beta", d), Mp3(tmp, "c.mp3", "Alfa", d), Mp3(tmp, "d.mp3", "Beta", d));
        var options = Rule();
        options.ResetNumberPerTag = "ID3.Album";

        var plan = RenamePlanner.Build(entries, options, TestHelpers.NoDisk);

        Assert.Equal(["01-a.mp3", "01-b.mp3", "02-c.mp3", "02-d.mp3"], Names(plan));
    }

    [Fact]
    public void PerTag_FilesWithoutTag_ShareTheirOwnGroup()
    {
        using var tmp = new TempFolder();
        var d = new DateTime(2026, 3, 1);
        var entries = Entries(
            Mp3(tmp, "a.mp3", "Alfa", d), tmp.File("n1.txt"), tmp.File("n2.txt"), Mp3(tmp, "b.mp3", "Alfa", d));
        var options = Rule();
        options.ResetNumberPerTag = "ID3.Album";

        var plan = RenamePlanner.Build(entries, options, TestHelpers.NoDisk);

        Assert.Equal(["01-a.mp3", "01-n1.txt", "02-n2.txt", "02-b.mp3"], Names(plan));
    }

    [Fact]
    public void PerTag_UsesTheGivenTagReader()
    {
        var fake = new MediaFixtures.FakeTags(MediaFixtures.Text("ID3.Album", "Hep Ayni"));
        var entries = new List<FileEntry> { TestHelpers.File("a.mp3"), TestHelpers.File("b.mp3") };
        var options = Rule();
        options.ResetNumberPerTag = "ID3.Album";
        options.TagReader = fake;

        var plan = RenamePlanner.Build(entries, options, TestHelpers.NoDisk);

        Assert.Equal(["01-a.mp3", "02-b.mp3"], Names(plan));
        Assert.True(fake.Reads >= 2);
    }

    [Fact]
    public void DateAndTagTogether_GroupByBoth()
    {
        using var tmp = new TempFolder();
        var d1 = new DateTime(2026, 3, 1);
        var d2 = new DateTime(2026, 3, 2);
        var entries = Entries(
            Mp3(tmp, "a.mp3", "Alfa", d1), Mp3(tmp, "b.mp3", "Alfa", d2), Mp3(tmp, "c.mp3", "Beta", d1),
            Mp3(tmp, "d.mp3", "Alfa", d1), Mp3(tmp, "e.mp3", "Beta", d1));
        var options = Rule();
        options.ResetNumberPerDate = true;
        options.ResetNumberPerTag = "ID3.Album";

        var plan = RenamePlanner.Build(entries, options, TestHelpers.NoDisk);

        // (d1,Alfa): a,d  (d2,Alfa): b  (d1,Beta): c,e
        Assert.Equal(["01-a.mp3", "01-b.mp3", "01-c.mp3", "02-d.mp3", "02-e.mp3"], Names(plan));
    }

    [Fact]
    public void FolderAndTag_Combine()
    {
        var fake = new MediaFixtures.FakeTags(MediaFixtures.Text("ID3.Album", "A"));
        var entries = new List<FileEntry>
        {
            TestHelpers.File("a.mp3", @"C:\x"), TestHelpers.File("b.mp3", @"C:\y"), TestHelpers.File("c.mp3", @"C:\x"),
        };
        var options = Rule();
        options.ResetNumberPerFolder = true;
        options.ResetNumberPerTag = "ID3.Album";
        options.TagReader = fake;

        Assert.Equal(["01-a.mp3", "01-b.mp3", "02-c.mp3"], Names(RenamePlanner.Build(entries, options, TestHelpers.NoDisk)));
    }

    [Fact]
    public void FolderOnly_BehavesAsBefore()
    {
        var entries = new List<FileEntry>
        {
            TestHelpers.File("a.txt", @"C:\x"), TestHelpers.File("b.txt", @"C:\y"), TestHelpers.File("c.txt", @"C:\x"),
        };
        var options = Rule();
        options.ResetNumberPerFolder = true;

        Assert.Equal(["01-a.txt", "01-b.txt", "02-c.txt"], Names(RenamePlanner.Build(entries, options, TestHelpers.NoDisk)));
    }

    [Fact]
    public void TagRename_RestartsPerDateAndPerTag()
    {
        using var tmp = new TempFolder();
        var d1 = new DateTime(2026, 3, 1);
        var d2 = new DateTime(2026, 3, 2);
        var entries = Entries(
            Mp3(tmp, "a.mp3", "Alfa", d1), Mp3(tmp, "b.mp3", "Beta", d1), Mp3(tmp, "c.mp3", "Alfa", d2), Mp3(tmp, "d.mp3", "Alfa", d1));

        var byTag = new TagRenameOptions { Template = "<ID3.Album>-??.\\e", ResetNumberPerTag = "ID3.Album" };
        Assert.Equal(
            ["Alfa-01.mp3", "Beta-01.mp3", "Alfa-02.mp3", "Alfa-03.mp3"],
            Names(TagRenamePlanner.Build(entries, byTag, null, TestHelpers.NoDisk)));

        var byDate = new TagRenameOptions { Template = "<ID3.Album>-??.\\e", ResetNumberPerDate = true };
        Assert.Equal(
            ["Alfa-01.mp3", "Beta-02.mp3", "Alfa-01.mp3", "Alfa-03.mp3"],
            Names(TagRenamePlanner.Build(entries, byDate, null, TestHelpers.NoDisk)));

        var both = new TagRenameOptions { Template = "<ID3.Album>-??.\\e", ResetNumberPerDate = true, ResetNumberPerTag = "ID3.Album" };
        Assert.Equal(
            ["Alfa-01.mp3", "Beta-01.mp3", "Alfa-01.mp3", "Alfa-02.mp3"],
            Names(TagRenamePlanner.Build(entries, both, null, TestHelpers.NoDisk)));
    }

    [Fact]
    public void TagRename_FolderOnly_BehavesAsBefore()
    {
        var options = new TagRenameOptions { Template = "??.\\e", ResetNumberPerFolder = true, Missing = MissingTagPolicy.UseEmpty };
        var plan = TagRenamePlanner.Build(
            [TestHelpers.File("a.mp3", @"C:\x"), TestHelpers.File("b.mp3", @"C:\y"), TestHelpers.File("c.mp3", @"C:\x")],
            options, new MediaFixtures.FakeTags(), TestHelpers.NoDisk);

        Assert.Equal(["01.mp3", "01.mp3", "02.mp3"], Names(plan));
    }
}
