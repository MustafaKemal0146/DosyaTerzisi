using DosyaTerzisi.Core;

namespace DosyaTerzisi.Tests;

/// <summary>A history file in a temp folder that is removed afterwards.</summary>
public sealed class TempHistory : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "dosyaterzisi-hist-" + Guid.NewGuid().ToString("N"));

    public string Path_ => System.IO.Path.Combine(_dir, "history.json");

    public HistoryStore Open() => new(Path_);

    public RenameService Service() => new(Open());

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }
}

public class HistoryTests
{
    private static IReadOnlyList<RenameItem> Plan(TempFolder tmp, RenameOptions options, ScanOptions? scan = null) =>
        RenamePlanner.Build(FileScanner.Scan(tmp.Root, scan ?? new ScanOptions()).Entries, options);

    private static RenameOptions Prefix(string prefix) =>
        new() { Method = RenameMethod.Wildcard, Search = "*", Replace = prefix + @"\1" };

    // ---- Persistence ----------------------------------------------------------------------------------------

    [Fact]
    public void Entries_SurviveRestart()
    {
        using var tmp = new TempFolder();
        using var hist = new TempHistory();
        tmp.File("a.txt");

        hist.Service().Execute(Plan(tmp, Prefix("x_")), "ön ek");

        var entries = hist.Open().Entries;
        var entry = Assert.Single(entries);
        Assert.Equal("ön ek", entry.Description);
        Assert.Equal(OperationState.Completed, entry.State);
        Assert.Equal(new RenameMove(tmp.Root, "a.txt", "x_a.txt", false), entry.Moves.Single());
    }

    [Fact]
    public void Undo_WorksAfterRestart()
    {
        using var tmp = new TempFolder();
        using var hist = new TempHistory();
        tmp.File("Öğrenci.txt", "içerik");
        hist.Service().Execute(Plan(tmp, Prefix("x_")), "ön ek");

        var afterRestart = hist.Service();
        Assert.True(afterRestart.CanUndo);
        var undo = afterRestart.UndoLatest()!.Value;

        Assert.Empty(undo.Result.Failures);
        Assert.Equal(["Öğrenci.txt"], tmp.Tree());
        Assert.False(hist.Service().CanUndo);
    }

    [Fact]
    public void Undo_GoesBackStepByStep()
    {
        using var tmp = new TempFolder();
        using var hist = new TempHistory();
        tmp.File("a.txt");
        var service = hist.Service();

        service.Execute(Plan(tmp, Prefix("1_")), "bir");
        service.Execute(Plan(tmp, Prefix("2_")), "iki");
        Assert.Equal(["2_1_a.txt"], tmp.Tree());

        Assert.Equal("iki", service.UndoLatest()!.Value.Entry.Description);
        Assert.Equal(["1_a.txt"], tmp.Tree());

        Assert.Equal("bir", service.UndoLatest()!.Value.Entry.Description);
        Assert.Equal(["a.txt"], tmp.Tree());

        Assert.Null(service.UndoLatest());
    }

    [Fact]
    public void NothingToRename_LeavesNoEntry()
    {
        using var tmp = new TempFolder();
        using var hist = new TempHistory();
        tmp.File("a.txt");

        var result = hist.Service().Execute(Plan(tmp, new RenameOptions { Search = "*", Replace = @"\1" }), "hiçbiri");

        Assert.Empty(result.Journal.Moves);
        Assert.Empty(hist.Open().Entries);
    }

    [Fact]
    public void PartlyFailedRename_RecordsOnlyWhatHappened()
    {
        using var tmp = new TempFolder();
        using var hist = new TempHistory();
        var locked = tmp.File("a.txt");
        tmp.File("b.txt");

        using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var result = hist.Service().Execute(Plan(tmp, Prefix("x_")), "kilitli");
            Assert.Single(result.Failures);
        }

        var entry = Assert.Single(hist.Open().Entries);
        Assert.Equal(["x_b.txt"], entry.Moves.Select(m => m.NewName));
    }

    [Fact]
    public void PartlyFailedUndo_KeepsTheRemainingMoves_ForANextTry()
    {
        using var tmp = new TempFolder();
        using var hist = new TempHistory();
        tmp.File("a.txt");
        tmp.File("b.txt");
        var service = hist.Service();
        service.Execute(Plan(tmp, Prefix("x_")), "ön ek");

        using (new FileStream(Path.Combine(tmp.Root, "x_a.txt"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var undo = service.UndoLatest()!.Value;
            Assert.Single(undo.Result.Failures);
        }

        Assert.Equal(["b.txt", "x_a.txt"], tmp.Tree());
        var entry = Assert.Single(service.History.Entries);
        Assert.Equal(["x_a.txt"], entry.Moves.Select(m => m.NewName));

        service.UndoLatest();

        Assert.Equal(["a.txt", "b.txt"], tmp.Tree());
        Assert.False(service.CanUndo);
    }

    [Fact]
    public void FilesRemovedByTheUser_DoNotStayInTheHistoryForever()
    {
        using var tmp = new TempFolder();
        using var hist = new TempHistory();
        tmp.File("a.txt");
        var service = hist.Service();
        service.Execute(Plan(tmp, Prefix("x_")), "ön ek");
        File.Delete(Path.Combine(tmp.Root, "x_a.txt"));

        var undo = service.UndoLatest()!.Value;

        Assert.Single(undo.Result.Failures);
        Assert.False(service.CanUndo);
    }

    [Fact]
    public void CorruptFile_IsSetAside_AndStartsEmpty()
    {
        using var hist = new TempHistory();
        Directory.CreateDirectory(Path.GetDirectoryName(hist.Path_)!);
        File.WriteAllText(hist.Path_, "{ this is not json");

        var store = hist.Open();

        Assert.Empty(store.Entries);
        Assert.NotNull(store.LoadWarning);
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(hist.Path_)!, "history.json.bad-*"));
    }

    [Fact]
    public void OnlyTheNewestEntriesAreKept()
    {
        using var hist = new TempHistory();
        var store = hist.Open();

        for (int i = 0; i < HistoryStore.MaxEntries + 10; i++)
        {
            var entry = store.Begin("op " + i, [new RenameMove(@"C:\x", "a", "b", false)]);
            store.Complete(entry.Id, entry.Moves);
        }

        Assert.Equal(HistoryStore.MaxEntries, store.Entries.Count);
        Assert.Equal("op 10", store.Entries[0].Description);
    }

    [Fact]
    public void UnwritableHistory_StopsTheRenameBeforeAnythingIsTouched()
    {
        using var tmp = new TempFolder();
        using var hist = new TempHistory();
        tmp.File("a.txt");
        var service = hist.Service();
        // A folder where the history file should be makes every save fail.
        Directory.CreateDirectory(hist.Path_ + ".tmp");

        Assert.ThrowsAny<Exception>(() => service.Execute(Plan(tmp, Prefix("x_")), "ön ek"));

        Assert.Equal(["a.txt"], tmp.Tree());
    }

    // ---- Recovery -------------------------------------------------------------------------------------------

    [Fact]
    public void Recovery_FindsWhichRenamesHappened_AndAllowsUndo()
    {
        using var tmp = new TempFolder();
        using var hist = new TempHistory();
        tmp.File("a.txt");
        tmp.File("b.txt");
        tmp.File("c.txt");

        // Planned: all three. The program "crashed" after the first one.
        var store = hist.Open();
        var planned = new[] { "a", "b", "c" }.Select(n => new RenameMove(tmp.Root, n + ".txt", "x_" + n + ".txt", false)).ToList();
        store.Begin("yarım", planned);
        File.Move(Path.Combine(tmp.Root, "a.txt"), Path.Combine(tmp.Root, "x_a.txt"));

        var service = hist.Service();
        var report = Assert.Single(service.RecoverInterrupted());

        Assert.Equal(3, report.Planned);
        Assert.Equal(1, report.Done);
        Assert.Empty(report.Problems);
        Assert.Empty(service.History.Interrupted);

        service.UndoLatest();
        Assert.Equal(["a.txt", "b.txt", "c.txt"], tmp.Tree());
    }

    [Fact]
    public void Recovery_PutsParkedFilesBack_AfterAnInterruptedSwap()
    {
        using var tmp = new TempFolder();
        using var hist = new TempHistory();
        tmp.File("a-b.txt", "first");
        tmp.File("b-a.txt", "second");

        var store = hist.Open();
        var entry = store.Begin("takas",
        [
            new RenameMove(tmp.Root, "a-b.txt", "b-a.txt", false),
            new RenameMove(tmp.Root, "b-a.txt", "a-b.txt", false),
        ]);
        string tag = entry.Id.ToString("N");
        // Phase one of the swap finished, phase two never started.
        File.Move(Path.Combine(tmp.Root, "a-b.txt"), Path.Combine(tmp.Root, RenameExecutor.TempName(tag, "a-b.txt")));
        File.Move(Path.Combine(tmp.Root, "b-a.txt"), Path.Combine(tmp.Root, RenameExecutor.TempName(tag, "b-a.txt")));

        var report = Assert.Single(hist.Service().RecoverInterrupted());

        Assert.Equal(2, report.Restored);
        Assert.Equal(0, report.Done);
        Assert.Empty(report.Problems);
        Assert.Equal(["a-b.txt", "b-a.txt"], tmp.Tree());
        Assert.Equal("first", File.ReadAllText(Path.Combine(tmp.Root, "a-b.txt")));
        Assert.Empty(hist.Open().Entries);
    }

    [Fact]
    public void Recovery_FollowsRenamedParentFolders()
    {
        using var tmp = new TempFolder();
        using var hist = new TempHistory();
        tmp.File(@"Eski\a.txt");
        tmp.File(@"Eski\iç\b.txt");

        var options = new RenameOptions { Method = RenameMethod.Wildcard, Search = "*", Replace = "N_" + @"\1" };
        var scan = new ScanOptions { IncludeFolders = true, MaxDepth = ScanOptions.Unlimited };
        var items = Plan(tmp, options, scan);

        // The whole rename finished, but the program died before it could mark the entry completed.
        var store = hist.Open();
        var entry = store.Begin("hepsi", items.Select(i => new RenameMove(i.Entry.Directory, i.Entry.Name, i.NewName, i.Entry.IsDirectory)));
        RenameExecutor.Execute(items, entry.Id.ToString("N"));
        Assert.Equal(["N_Eski", @"N_Eski\N_a.txt", @"N_Eski\N_iç", @"N_Eski\N_iç\N_b.txt"], tmp.Tree().Order(StringComparer.Ordinal));

        var service = hist.Service();
        var report = Assert.Single(service.RecoverInterrupted());

        Assert.Equal(4, report.Done);
        Assert.Empty(report.Problems);

        var undo = service.UndoLatest()!.Value;
        Assert.Empty(undo.Result.Failures);
        Assert.Equal(["Eski", @"Eski\a.txt", @"Eski\iç", @"Eski\iç\b.txt"], tmp.Tree().Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Recovery_ReportsFilesItCannotAccountFor()
    {
        using var tmp = new TempFolder();
        using var hist = new TempHistory();
        hist.Open().Begin("kayıp", [new RenameMove(tmp.Root, "gone.txt", "new.txt", false)]);

        var report = Assert.Single(hist.Service().RecoverInterrupted());

        Assert.Equal(0, report.Done);
        Assert.Single(report.Problems);
        Assert.Empty(hist.Open().Entries);
    }

    [Fact]
    public void Recovery_HasNothingToDo_WhenEverythingFinished()
    {
        using var tmp = new TempFolder();
        using var hist = new TempHistory();
        tmp.File("a.txt");
        hist.Service().Execute(Plan(tmp, Prefix("x_")), "tamam");

        Assert.Empty(hist.Service().RecoverInterrupted());
        Assert.True(hist.Service().CanUndo);
    }
}
