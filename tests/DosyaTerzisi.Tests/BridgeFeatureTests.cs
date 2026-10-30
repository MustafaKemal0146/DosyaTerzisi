using DosyaTerzisi.Bridge.Contract;
using Core = DosyaTerzisi.Core;

namespace DosyaTerzisi.Tests;

/// <summary>Etiketten adlandırma, öznitelikler, numaralı nesneler, sürükle-bırak ve klasör ağacı; hepsi gerçek dosyalarla, JSON üzerinden.</summary>
public class BridgeFeatureTests
{
    private static readonly DateTime Jan1 = new(2020, 1, 1, 12, 0, 0);

    private static long Ms(DateTime local) => new DateTimeOffset(local).ToUnixTimeMilliseconds();

    private static TagPreviewParams Tag(string template, TagMissing missing = TagMissing.Skip, bool auto = false, params int[] selected) =>
        new(template, missing, 1, 1, false, auto, false, [.. selected], Operation.Rename, "", NumberStyle.Decimal, false, "", false, false);

    private static readonly TimeRule KeepTime = new(TimeAction.Keep, 0, 0, TimeKind.Modified, 0, 0, 0, 0);

    private static AttributeRule Attrs(
        TriState readOnly = TriState.Keep, TriState hidden = TriState.Keep, TimeRule? modified = null, TimeRule? created = null,
        bool folders = true) =>
        new(readOnly, TriState.Keep, hidden, TriState.Keep, modified ?? KeepTime, created ?? KeepTime, KeepTime, folders);

    // ---- Etiketten adlandırma ------------------------------------------------------------------------------------

    private static void Songs(BridgeHarness h)
    {
        MediaFixtures.WriteMp3(Path.Combine(h.Root, "01.mp3"), t => { t.Performers = ["Sezen Aksu"]; t.Title = "Şarkı Bitti"; t.Track = 4; });
        MediaFixtures.WriteMp3(Path.Combine(h.Root, "02.mp3"), t => { t.Performers = ["AC/DC"]; t.Title = "Back In Black"; t.Track = 6; });
        MediaFixtures.WriteMp3(Path.Combine(h.Root, "03.mp3"));
        h.Files.File("not.txt");
    }

    [Fact]
    public async Task TagPreview_NamesFilesFromTheirTags_AndSkipsTheOnesWithoutTags()
    {
        using var h = new BridgeHarness();
        Songs(h);
        await h.Scan(new ListingOptions(true, false, "*.mp3", false, 10));

        var preview = await h.Call<Preview>("tagPreview", Tag(@"<ID3.TrackNumber:00> <ID3.Artist> - <ID3.Track>.\e"));

        Assert.Equal(["04 Sezen Aksu - Şarkı Bitti.mp3", "06 AC_DC - Back In Black.mp3", "03.mp3"], preview.Rows.Select(r => r.NewName));
        Assert.Equal([RowStatus.Ready, RowStatus.Ready, RowStatus.MissingTag], preview.Rows.Select(r => r.Status));
        Assert.Contains("ID3.Artist", preview.Rows[2].Message);
        Assert.Equal(2, preview.Ready);
        Assert.Equal(0, preview.Problems);
    }

    [Fact]
    public async Task TagPreview_MissingTagsCanBeLeftEmpty_AndSelectionLimitsTheRows()
    {
        using var h = new BridgeHarness();
        Songs(h);
        await h.Scan(new ListingOptions(true, false, "*.mp3", false, 10));

        var empty = await h.Call<Preview>("tagPreview", Tag(@"<ID3.Track>.\e", TagMissing.UseEmpty));
        Assert.Equal(".mp3", empty.Rows[2].NewName);

        var onlyFirst = await h.Call<Preview>("tagPreview", Tag(@"<ID3.Track>.\e", selected: 0));
        Assert.Equal(["Şarkı Bitti.mp3", "02.mp3", "03.mp3"], onlyFirst.Rows.Select(r => r.NewName));
    }

    [Fact]
    public async Task TagRename_RenamesOnDisk_RecordsHistory_AndUndoes()
    {
        using var h = new BridgeHarness();
        Songs(h);
        await h.Scan(new ListingOptions(true, false, "*.mp3", false, 10));
        await h.Call<Preview>("tagPreview", Tag(@"<ID3.Artist> - <ID3.Track>.\e"));

        var outcome = await h.Rename(description: "Etiket: sanatçı - şarkı");

        Assert.Equal(2, outcome.Renamed);
        Assert.Equal(["03.mp3", "AC_DC - Back In Black.mp3", "Sezen Aksu - Şarkı Bitti.mp3", "not.txt"], h.Files.Tree());
        Assert.Equal("Etiket: sanatçı - şarkı", (await h.Call<List<HistoryItem>>("history", new Empty()))[0].Description);

        await h.Call<UndoOutcome?>("undo", new Empty());
        Assert.Equal(["01.mp3", "02.mp3", "03.mp3", "not.txt"], h.Files.Tree());
    }

    [Fact]
    public async Task TagRename_SameTagsTwice_ConflictsOrGetsNumbered()
    {
        using var h = new BridgeHarness();
        MediaFixtures.WriteMp3(Path.Combine(h.Root, "a.mp3"), t => t.Title = "Aynı");
        MediaFixtures.WriteMp3(Path.Combine(h.Root, "b.mp3"), t => t.Title = "Aynı");
        await h.Scan();

        var reported = await h.Call<Preview>("tagPreview", Tag(@"<ID3.Track>.\e"));
        Assert.Equal([RowStatus.Ready, RowStatus.Conflict], reported.Rows.Select(r => r.Status));

        var numbered = await h.Call<Preview>("tagPreview", Tag(@"<ID3.Track>.\e", auto: true));
        Assert.Equal(["Aynı.mp3", "Aynı(01).mp3"], numbered.Rows.Select(r => r.NewName));
    }

    [Fact]
    public async Task ReadTags_ListsTheTagsOfOneFile()
    {
        using var h = new BridgeHarness();
        Songs(h);
        await h.Scan(new ListingOptions(true, true, "*", false, 10));
        var rows = (await h.Scan()).Rows;
        int song = rows.First(r => r.Name == "01.mp3").Id;
        int text = rows.First(r => r.Name == "not.txt").Id;

        var tags = await h.Call<TagList>("readTags", new ReadTagsParams(song, false));

        Assert.Null(tags.Error);
        Assert.Contains(tags.Entries, e => e.Name == "ID3.Artist" && e.Value == "Sezen Aksu");
        Assert.Contains(tags.Entries, e => e.Name == "ID3.TrackNumber" && e.Value == "4");
        Assert.Equal(tags.Entries.Select(e => e.Name).Order(StringComparer.OrdinalIgnoreCase), tags.Entries.Select(e => e.Name));

        Assert.Empty((await h.Call<TagList>("readTags", new ReadTagsParams(text, false))).Entries.Where(e => !e.Name.StartsWith("PROP.", StringComparison.OrdinalIgnoreCase)));

        var error = await Assert.ThrowsAsync<BridgeError>(() => h.Call<TagList>("readTags", new ReadTagsParams(99, false)));
        Assert.Equal(ErrorCodes.BadRequest, error.Code);
    }

    [Fact]
    public async Task ReadTags_ReportsADamagedFile()
    {
        using var h = new BridgeHarness();
        h.Files.File("bozuk.jpg", "bu bir resim değil");
        await h.Scan();

        var tags = await h.Call<TagList>("readTags", new ReadTagsParams(0, false));

        Assert.NotNull(tags.Error);
        Assert.Empty(tags.Entries);
    }

    // ---- Öznitelikler ve tarihler --------------------------------------------------------------------------------

    [Fact]
    public async Task AttributePreview_ShowsBeforeAndAfter_ForEveryRow()
    {
        using var h = new BridgeHarness();
        var a = h.Files.File("a.txt");
        h.Files.File("b.txt");
        File.SetLastWriteTime(a, Jan1);
        await h.Scan();

        var preview = await h.Call<AttributePreview>(
            "attributePreview",
            new AttributePreviewParams(Attrs(readOnly: TriState.Set, modified: new TimeRule(TimeAction.Shift, 0, 90, TimeKind.Modified, 0, 0, 0, 0)), [0]));

        Assert.Equal(1, preview.Changed);
        Assert.Equal(0, preview.Problems);
        var first = preview.Rows[0];
        Assert.Equal(AttributeRowStatus.Ready, first.Status);
        Assert.False(first.Before.ReadOnly);
        Assert.True(first.After.ReadOnly);
        Assert.Equal(Ms(Jan1), first.ModifiedBefore);
        Assert.Equal(Ms(Jan1.AddMinutes(90)), first.ModifiedAfter);

        var other = preview.Rows[1];
        Assert.Equal(AttributeRowStatus.Unchanged, other.Status);
        Assert.Equal(other.ModifiedBefore, other.ModifiedAfter);
        Assert.Equal(other.Before, other.After);
    }

    [Fact]
    public async Task ApplyAttributes_ChangesTheDisk_AndUndoPutsItBack()
    {
        using var h = new BridgeHarness();
        var path = h.Files.File("a.txt");
        File.SetLastWriteTime(path, Jan1);
        await h.Scan();
        var rule = new AttributePreviewParams(
            Attrs(hidden: TriState.Set, modified: new TimeRule(TimeAction.SetTo, Ms(Jan1.AddYears(3)), 0, TimeKind.Modified, 0, 0, 0, 0)), []);

        var outcome = await h.Call<AttributeOutcome>("applyAttributes", rule);

        Assert.Equal(1, outcome.Changed);
        Assert.Empty(outcome.Failures);
        Assert.True(File.GetAttributes(path).HasFlag(FileAttributes.Hidden));
        Assert.Equal(Jan1.AddYears(3), File.GetLastWriteTime(path));

        var undo = await h.Call<AttributeUndo?>("undoAttributes", new Empty());

        Assert.NotNull(undo);
        Assert.Equal(1, undo.Restored);
        Assert.False(File.GetAttributes(path).HasFlag(FileAttributes.Hidden));
        Assert.Equal(Jan1, File.GetLastWriteTime(path));
        Assert.Null(await h.Call<AttributeUndo?>("undoAttributes", new Empty()));
    }

    [Fact]
    public async Task Attributes_UndoStepsBackThroughSeveralChanges()
    {
        using var h = new BridgeHarness();
        var path = h.Files.File("a.txt");
        await h.Scan();

        await h.Call<AttributeOutcome>("applyAttributes", new AttributePreviewParams(Attrs(hidden: TriState.Set), []));
        await h.Call<AttributeOutcome>("applyAttributes", new AttributePreviewParams(Attrs(readOnly: TriState.Set), []));
        Assert.Equal(FileAttributes.Hidden | FileAttributes.ReadOnly | FileAttributes.Archive, File.GetAttributes(path) & ~FileAttributes.Normal);

        await h.Call<AttributeUndo?>("undoAttributes", new Empty());
        Assert.False(File.GetAttributes(path).HasFlag(FileAttributes.ReadOnly));
        Assert.True(File.GetAttributes(path).HasFlag(FileAttributes.Hidden));

        await h.Call<AttributeUndo?>("undoAttributes", new Empty());
        Assert.False(File.GetAttributes(path).HasFlag(FileAttributes.Hidden));
    }

    [Fact]
    public async Task Attributes_CanLeaveFoldersAlone()
    {
        using var h = new BridgeHarness();
        h.Files.File("a.txt");
        h.Files.Dir("klasor");
        await h.Scan(new ListingOptions(true, true, "*", false, 10));

        var preview = await h.Call<AttributePreview>("attributePreview", new AttributePreviewParams(Attrs(hidden: TriState.Set, folders: false), []));

        Assert.Equal(1, preview.Changed);
        Assert.Equal([AttributeRowStatus.Unchanged, AttributeRowStatus.Ready], preview.Rows.Select(r => r.Status));
    }

    [Fact]
    public async Task Attributes_ImpossibleTimes_AreErrorRows()
    {
        using var h = new BridgeHarness();
        h.Files.File("a.txt");
        await h.Scan();

        var preview = await h.Call<AttributePreview>(
            "attributePreview",
            new AttributePreviewParams(Attrs(created: new TimeRule(TimeAction.Shift, 0, -60L * 24 * 365 * 1000, TimeKind.Created, 0, 0, 0, 0)), []));

        Assert.Equal(1, preview.Problems);
        Assert.Equal(AttributeRowStatus.Error, preview.Rows[0].Status);
        Assert.NotNull(preview.Rows[0].Message);
    }

    [Fact]
    public async Task Attributes_PhotoTakenDate_ComesFromTheExifOfTheFile()
    {
        using var h = new BridgeHarness();
        MediaFixtures.WriteJpeg(Path.Combine(h.Root, "IMG_1.jpg"), MediaFixtures.PhotoMetadata);
        MediaFixtures.WriteJpeg(Path.Combine(h.Root, "IMG_2.jpg"), new Dictionary<string, object>());
        await h.Scan();
        var rule = new AttributePreviewParams(Attrs(modified: new TimeRule(TimeAction.PhotoTaken, 0, 0, TimeKind.Modified, 0, 0, 0, 0)), []);

        var preview = await h.Call<AttributePreview>("attributePreview", rule);

        var taken = new DateTime(2019, 8, 17, 14, 30, 5);
        Assert.Equal(Ms(taken), preview.Rows[0].ModifiedAfter);
        Assert.Equal(AttributeRowStatus.Ready, preview.Rows[0].Status);
        Assert.Equal(AttributeRowStatus.Unchanged, preview.Rows[1].Status);
        Assert.Equal("Çekim tarihi bulunamadı.", preview.Rows[1].Message);

        await h.Call<AttributeOutcome>("applyAttributes", rule);
        Assert.Equal(taken, File.GetLastWriteTime(Path.Combine(h.Root, "IMG_1.jpg")));
    }

    [Fact]
    public async Task ApplyAttributes_ReportsAFileThatDisappeared()
    {
        using var h = new BridgeHarness();
        h.Files.File("a.txt");
        h.Files.File("b.txt");
        await h.Scan();
        File.Delete(Path.Combine(h.Root, "a.txt"));

        var outcome = await h.Call<AttributeOutcome>("applyAttributes", new AttributePreviewParams(Attrs(hidden: TriState.Set), []));

        Assert.Equal(1, outcome.Changed);
        Assert.EndsWith("a.txt", Assert.Single(outcome.Failures).Path);
    }

    // ---- Numaralı nesneler ---------------------------------------------------------------------------------------

    private NumberedOptions Numbered(BridgeHarness h, ObjectKind kind = ObjectKind.EmptyFile, string name = "Hafta ??.txt", int amount = 3,
        string size = "", string source = "") => new(kind, h.Root, name, amount, 1, 1, size, source, NumberStyle.Decimal);

    [Fact]
    public async Task NumberedPreview_ListsTheNames_AndMarksWhatExists()
    {
        using var h = new BridgeHarness();
        h.Files.File("Hafta 02.txt");

        var plan = await h.Call<NumberedPlan>("numberedPreview", Numbered(h));

        Assert.Null(plan.Problem);
        Assert.Equal(["Hafta 01.txt", "Hafta 02.txt", "Hafta 03.txt"], plan.Items.Select(i => i.Name));
        Assert.Equal([PlannedStatus.Ready, PlannedStatus.Exists, PlannedStatus.Ready], plan.Items.Select(i => i.Status));
        Assert.Equal(2, plan.Ready);
    }

    [Fact]
    public async Task NumberedPreview_ReportsProblems()
    {
        using var h = new BridgeHarness();

        Assert.Contains("1 ile", (await h.Call<NumberedPlan>("numberedPreview", Numbered(h, amount: 0))).Problem);
        Assert.Contains("Boyut anlaşılamadı", (await h.Call<NumberedPlan>("numberedPreview", Numbered(h, size: "çok büyük"))).Problem);
        Assert.Contains("bulunamadı", (await h.Call<NumberedPlan>("numberedPreview", Numbered(h, ObjectKind.CopyOfFile, source: Path.Combine(h.Root, "yok.docx")))).Problem);
        Assert.Contains("bulunamadı", (await h.Call<NumberedPlan>("numberedPreview", Numbered(h) with { Folder = Path.Combine(h.Root, "yok") })).Problem);
    }

    [Fact]
    public async Task CreateNumbered_MakesFilesOfTheRightSize()
    {
        using var h = new BridgeHarness();

        var outcome = await h.Call<CreateOutcome>("createNumbered", Numbered(h, size: "2K"));

        Assert.Equal(3, outcome.Created);
        Assert.Equal(["Hafta 01.txt", "Hafta 02.txt", "Hafta 03.txt"], h.Files.Tree());
        Assert.All(h.Files.Tree(), n => Assert.Equal(2048, new FileInfo(Path.Combine(h.Root, n)).Length));
    }

    [Fact]
    public async Task CreateNumbered_CopiesFilesAndFolders_AndNeverOverwrites()
    {
        using var h = new BridgeHarness();
        var template = Path.Combine(h.Root, "sablon", "sablon.docx");
        h.Files.File(@"sablon\sablon.docx", "şablon içeriği");
        h.Files.File("Ödev 2.docx", "dokunma");
        var target = Path.Combine(h.Root, "teslim");
        Directory.CreateDirectory(target);

        var files = await h.Call<CreateOutcome>(
            "createNumbered", new NumberedOptions(ObjectKind.CopyOfFile, h.Root, "Ödev ?.docx", 3, 1, 1, "", template, NumberStyle.Decimal));
        Assert.Equal(2, files.Created);
        Assert.Equal("dokunma", File.ReadAllText(Path.Combine(h.Root, "Ödev 2.docx")));
        Assert.Equal("şablon içeriği", File.ReadAllText(Path.Combine(h.Root, "Ödev 1.docx")));

        var folders = await h.Call<CreateOutcome>(
            "createNumbered", new NumberedOptions(ObjectKind.CopyOfFolder, target, "Kopya ?", 2, 1, 1, "", Path.Combine(h.Root, "sablon"), NumberStyle.Decimal));
        Assert.Equal(2, folders.Created);
        Assert.True(File.Exists(Path.Combine(target, "Kopya 2", "sablon.docx")));

        var empty = await h.Call<CreateOutcome>(
            "createNumbered", new NumberedOptions(ObjectKind.EmptyFolder, target, "Boş ??", 2, 1, 1, "", "", NumberStyle.Decimal));
        Assert.Equal(2, empty.Created);
        Assert.True(Directory.Exists(Path.Combine(target, "Boş 02")));
    }

    [Fact]
    public async Task CreateNumbered_WithAProblem_IsAnErrorAndCreatesNothing()
    {
        using var h = new BridgeHarness();

        var error = await Assert.ThrowsAsync<BridgeError>(() => h.Call<CreateOutcome>("createNumbered", Numbered(h, amount: 0)));

        Assert.Equal(ErrorCodes.Failed, error.Code);
        Assert.Empty(h.Files.Tree());
    }

    // ---- Dosya seçici, sürükle-bırak, yenileme ------------------------------------------------------------------

    [Fact]
    public async Task PickFile_UsesTheNativeDialog()
    {
        using var h = new BridgeHarness();
        h.Dialogs.OpenFile = @"C:\bir\dosya.docx";

        Assert.Equal(@"C:\bir\dosya.docx", await h.Call<string?>("pickFile", new PickFileParams("Seçin", "Tümü (*.*)|*.*")));

        h.Dialogs.OpenFile = null;
        Assert.Null(await h.Call<string?>("pickFile", new PickFileParams("Seçin", "Tümü (*.*)|*.*")));
    }

    [Fact]
    public async Task OpenPaths_ASingleFolder_IsReturnedAsTheFolderToOpen()
    {
        using var h = new BridgeHarness();
        h.Files.Dir("klasor");

        var result = await h.Call<OpenPathsResult>("openPaths", new OpenPathsParams([Path.Combine(h.Root, "klasor")]));

        Assert.Equal(Path.Combine(h.Root, "klasor"), result.Folder);
        Assert.Null(result.List);
    }

    [Fact]
    public async Task OpenPaths_Files_BecomeTheList_AndCanBeRenamed()
    {
        using var h = new BridgeHarness();
        h.Files.File("a.txt");
        h.Files.File("b.txt");
        h.Files.File(@"alt\c.txt");
        var chosen = new List<string> { Path.Combine(h.Root, "a.txt"), Path.Combine(h.Root, "alt", "c.txt"), Path.Combine(h.Root, "yok.txt") };

        var result = await h.Call<OpenPathsResult>("openPaths", new OpenPathsParams(chosen));

        Assert.Null(result.Folder);
        Assert.Equal(["a.txt", "c.txt"], result.List!.Rows.Select(r => r.Name));
        Assert.Equal(1, result.List.SkippedFolders);

        await h.Preview(BridgeHarness.Rule(RenameMethod.Wildcard, "*", @"x_\1"));
        await h.Rename(description: "sürüklenenler");

        Assert.Equal(["alt", "b.txt", "x_a.txt"], h.Files.Tree().Where(n => !n.Contains('\\')).ToArray());
        Assert.True(File.Exists(Path.Combine(h.Root, "alt", "x_c.txt")));
    }

    [Fact]
    public async Task Refresh_FollowsTheRenamedFiles_InAPathList_AndBackAfterUndo()
    {
        using var h = new BridgeHarness();
        h.Files.File("a.txt");
        h.Files.File("b.txt");
        await h.Call<OpenPathsResult>("openPaths", new OpenPathsParams([Path.Combine(h.Root, "a.txt"), Path.Combine(h.Root, "b.txt")]));
        await h.Preview(BridgeHarness.Rule(RenameMethod.Wildcard, "*", @"yeni_\1"));
        await h.Rename();

        var after = await h.Call<ScanResult>("refresh", new Empty());
        Assert.Equal(["yeni_a.txt", "yeni_b.txt"], after.Rows.Select(r => r.Name));

        await h.Call<UndoOutcome?>("undo", new Empty());
        var back = await h.Call<ScanResult>("refresh", new Empty());
        Assert.Equal(["a.txt", "b.txt"], back.Rows.Select(r => r.Name));
    }

    [Fact]
    public async Task Refresh_ReReadsTheFolder_WithTheSameOptions()
    {
        using var h = new BridgeHarness();
        h.Files.File("a.txt");
        h.Files.File("b.png");
        await h.Scan(new ListingOptions(true, false, "*.txt", false, 10));
        h.Files.File("c.txt");

        var refreshed = await h.Call<ScanResult>("refresh", new Empty());

        Assert.Equal(["a.txt", "c.txt"], refreshed.Rows.Select(r => r.Name));
    }

    [Fact]
    public async Task Refresh_WithoutAList_IsAnError_AndEmptyDropsAreRejected()
    {
        using var h = new BridgeHarness();

        Assert.Equal(ErrorCodes.Failed, (await Assert.ThrowsAsync<BridgeError>(() => h.Call<ScanResult>("refresh", new Empty()))).Code);
        Assert.Equal(ErrorCodes.BadRequest, (await Assert.ThrowsAsync<BridgeError>(() => h.Call<OpenPathsResult>("openPaths", new OpenPathsParams([])))).Code);
    }

    // ---- Klasör ağacı --------------------------------------------------------------------------------------------

    [Fact]
    public async Task ListFolders_Root_HasTheDrivesAndTheUsualFolders()
    {
        using var h = new BridgeHarness();

        var roots = await h.Call<FolderList>("listFolders", new ListFoldersParams(""));

        Assert.Equal("", roots.Path);
        Assert.Contains(roots.Folders, f => f.Name.StartsWith("Yerel Disk") || f.Name.Contains("(C:)"));
        Assert.All(roots.Folders, f => Assert.True(Directory.Exists(f.Path), f.Path));
        Assert.Equal(roots.Folders.Count, roots.Folders.Select(f => f.Path).Distinct().Count());
    }

    [Fact]
    public async Task ListFolders_ListsSubfolders_InNaturalOrder_SkippingHiddenOnes()
    {
        using var h = new BridgeHarness();
        h.Files.Dir("b10");
        h.Files.Dir("b2");
        h.Files.File(@"b2\icinde\x.txt");
        h.Files.Dir("gizli");
        File.SetAttributes(Path.Combine(h.Root, "gizli"), FileAttributes.Hidden | FileAttributes.Directory);
        h.Files.File("dosya.txt");

        var list = await h.Call<FolderList>("listFolders", new ListFoldersParams(h.Root));

        Assert.Equal(["b2", "b10"], list.Folders.Select(f => f.Name));
        Assert.Equal([true, false], list.Folders.Select(f => f.HasChildren));
        Assert.Equal(Path.Combine(h.Root, "b2"), list.Folders[0].Path);
    }

    [Fact]
    public async Task ListFolders_OfAMissingFolder_IsAnError()
    {
        using var h = new BridgeHarness();

        var error = await Assert.ThrowsAsync<BridgeError>(
            () => h.Call<FolderList>("listFolders", new ListFoldersParams(Path.Combine(h.Root, "yok"))));

        Assert.Equal(ErrorCodes.Failed, error.Code);
    }
}
