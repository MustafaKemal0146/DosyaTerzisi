using DosyaTerzisi.Core;

namespace DosyaTerzisi.Tests;

/// <summary>Copy, move into other folders (relative paths), shortcuts and hard links, with real files.</summary>
public class TransferTests
{
    private static RenameOptions Rule(string search, string replace, TransferKind kind = TransferKind.Rename, string? target = null) =>
        new() { Method = RenameMethod.Wildcard, Search = search, Replace = replace, Kind = kind, TargetFolder = target };

    private static IReadOnlyList<RenameItem> Plan(TempFolder tmp, RenameOptions options, ScanOptions? scan = null, PlanOptions? plan = null) =>
        RenamePlanner.Build(FileScanner.Scan(tmp.Root, scan ?? new ScanOptions()).Entries, options, plan);

    // ---- Relative paths: distributing into folders -------------------------------------------------------------

    [Fact]
    public void SlashInTheNewName_MovesTheFileIntoANewSubFolder()
    {
        using var tmp = new TempFolder();
        tmp.File("a.jpg", "A");

        var items = Plan(tmp, Rule("*.*", @"Resimler/\1.\2"));
        Assert.Equal(RenameStatus.Ready, items[0].Status);
        Assert.Equal("a.jpg", items[0].NewName);
        Assert.Equal(Path.Combine(tmp.Root, "Resimler"), items[0].DestinationDirectory);

        var result = RenameExecutor.Execute(items);

        Assert.Empty(result.Failures);
        Assert.Equal(["Resimler", Path.Combine("Resimler", "a.jpg")], tmp.Tree());
        Assert.Equal("A", File.ReadAllText(Path.Combine(tmp.Root, "Resimler", "a.jpg")));
    }

    [Fact]
    public void DotDot_GoesToTheParentFolder()
    {
        using var tmp = new TempFolder();
        tmp.File(Path.Combine("alt", "a.txt"), "A");

        var items = Plan(tmp, Rule("*.*", @"../\1.\2"), new ScanOptions { MaxDepth = 1 });
        var file = items.Single(i => i.Entry.Name == "a.txt");
        RenameExecutor.Execute([file]);

        Assert.Equal(["a.txt", "alt"], tmp.Tree());
    }

    [Fact]
    public void FilesIntoDateFolders_AndNumberingRestartsInEachNewFolder()
    {
        using var tmp = new TempFolder();
        string a = tmp.File("a.jpg");
        string b = tmp.File("b.jpg");
        string c = tmp.File("c.jpg");
        File.SetLastWriteTime(a, new DateTime(2026, 1, 15));
        File.SetLastWriteTime(b, new DateTime(2026, 1, 15));
        File.SetLastWriteTime(c, new DateTime(2026, 1, 16));

        var options = Rule("*.*", @"\Y-\m-\d/Resim??.\2");
        options.ResetNumberPerFolder = true;
        var items = Plan(tmp, options);

        Assert.Equal(["Resim01.jpg", "Resim02.jpg", "Resim01.jpg"], items.Select(i => i.NewName));
        RenameExecutor.Execute(items);
        Assert.Equal(
            [
                "2026-01-15",
                Path.Combine("2026-01-15", "Resim01.jpg"),
                Path.Combine("2026-01-15", "Resim02.jpg"),
                "2026-01-16",
                Path.Combine("2026-01-16", "Resim01.jpg"),
            ],
            tmp.Tree());
    }

    [Fact]
    public void ABadFolderName_IsReported_AndNothingHappens()
    {
        using var tmp = new TempFolder();
        tmp.File("a.txt");

        var items = Plan(tmp, Rule("*.*", @"CON/\1.\2"));

        Assert.Equal(RenameStatus.InvalidName, items[0].Status);
        Assert.Equal(["a.txt"], tmp.Tree());
    }

    [Fact]
    public void MovingOntoAnExistingFile_IsAConflict_NeverAnOverwrite()
    {
        using var tmp = new TempFolder();
        tmp.File("a.txt", "NEW");
        tmp.File(Path.Combine("Hedef", "a.txt"), "OLD");

        var items = Plan(tmp, Rule("*.*", @"Hedef/\1.\2"), new ScanOptions { MaxDepth = 0 }).Where(i => !i.Entry.IsDirectory).ToList();

        Assert.Equal(RenameStatus.Conflict, items.Single(i => i.Entry.Name == "a.txt").Status);
        Assert.Equal("OLD", File.ReadAllText(Path.Combine(tmp.Root, "Hedef", "a.txt")));
    }

    [Fact]
    public void MoveIsUndone_AndTheFoldersItCreatedAreRemoved()
    {
        using var tmp = new TempFolder();
        tmp.File("a.txt", "A");

        var result = RenameExecutor.Execute(Plan(tmp, Rule("*.*", @"x/y/\1.\2")));
        Assert.Equal(["x", Path.Combine("x", "y"), Path.Combine("x", "y", "a.txt")], tmp.Tree());

        var undo = RenameExecutor.Undo(result.Journal);

        Assert.Empty(undo.Failures);
        Assert.Equal(["a.txt"], tmp.Tree());
    }

    // ---- Copy ------------------------------------------------------------------------------------------------------

    [Fact]
    public void Copy_KeepsTheOriginal_AndUndoRemovesOnlyTheCopy()
    {
        using var tmp = new TempFolder();
        tmp.File("a.txt", "hello");

        var items = Plan(tmp, Rule("*.*", @"Kopya_\1.\2", TransferKind.Copy));
        var result = RenameExecutor.Execute(items);

        Assert.Empty(result.Failures);
        Assert.Equal(["Kopya_a.txt", "a.txt"], tmp.Tree().Order(StringComparer.Ordinal));
        Assert.Equal("hello", File.ReadAllText(Path.Combine(tmp.Root, "Kopya_a.txt")));

        var undo = RenameExecutor.Undo(result.Journal);

        Assert.Empty(undo.Failures);
        Assert.Equal(["a.txt"], tmp.Tree());
    }

    [Fact]
    public void Copy_ToAnotherFolder_KeepsTheSameName()
    {
        using var tmp = new TempFolder();
        string target = Path.Combine(tmp.Root, "Yedek");
        tmp.File("a.txt", "hello");
        Directory.CreateDirectory(target);

        var items = Plan(tmp, Rule("*.*", @"\1.\2", TransferKind.Copy, target), plan: null)
            .Where(i => !i.Entry.IsDirectory).ToList();
        RenameExecutor.Execute(items);

        Assert.True(File.Exists(Path.Combine(target, "a.txt")));
        Assert.True(File.Exists(Path.Combine(tmp.Root, "a.txt")));
    }

    [Fact]
    public void CopyOverTheSameName_IsAConflict_OrNumberedOnRequest()
    {
        using var tmp = new TempFolder();
        tmp.File("a.txt", "hello");

        var report = Plan(tmp, Rule("*.*", @"\1.\2", TransferKind.Copy));
        var numbered = Plan(tmp, Rule("*.*", @"\1.\2", TransferKind.Copy), plan: new PlanOptions { Conflict = ConflictPolicy.AutoNumber });

        Assert.Equal(RenameStatus.Conflict, report[0].Status);
        Assert.Equal(RenameStatus.Ready, numbered[0].Status);
        Assert.Equal("a(01).txt", numbered[0].NewName);
    }

    [Fact]
    public void CopyOfAFolder_IsRecursive_AndUndoKeepsEditedFiles()
    {
        using var tmp = new TempFolder();
        tmp.File(Path.Combine("Proje", "a.txt"), "A");
        tmp.File(Path.Combine("Proje", "alt", "b.txt"), "B");

        var scan = new ScanOptions { IncludeFiles = false, IncludeFolders = true };
        var result = RenameExecutor.Execute(Plan(tmp, Rule("*", @"\1 - Kopya", TransferKind.Copy), scan));

        Assert.Empty(result.Failures);
        Assert.True(File.Exists(Path.Combine(tmp.Root, "Proje - Kopya", "alt", "b.txt")));

        // Someone edits a file in the copy: undo must not throw that work away.
        File.WriteAllText(Path.Combine(tmp.Root, "Proje - Kopya", "alt", "b.txt"), "DEĞİŞTİ, daha uzun");
        var undo = RenameExecutor.Undo(result.Journal);

        Assert.Single(undo.Failures);
        Assert.Equal("DEĞİŞTİ, daha uzun", File.ReadAllText(Path.Combine(tmp.Root, "Proje - Kopya", "alt", "b.txt")));
        Assert.False(File.Exists(Path.Combine(tmp.Root, "Proje - Kopya", "a.txt")));
        Assert.True(File.Exists(Path.Combine(tmp.Root, "Proje", "alt", "b.txt")));
    }

    [Fact]
    public void AFolderCannotBeCopiedIntoItself()
    {
        using var tmp = new TempFolder();
        tmp.Dir("Proje");

        var scan = new ScanOptions { IncludeFiles = false, IncludeFolders = true };
        var items = Plan(tmp, Rule("*", @"Proje/Kopya", TransferKind.Copy), scan);

        Assert.Equal(RenameStatus.InvalidName, items[0].Status);
        Assert.Contains("kendi içine", items[0].Message);
    }

    // ---- Shortcuts and hard links ----------------------------------------------------------------------------------

    [Fact]
    public void Shortcut_IsCreatedWithLnkExtension_AndUndoneByDeletingIt()
    {
        using var tmp = new TempFolder();
        string target = tmp.File("a.txt", "hello");

        var items = Plan(tmp, Rule("*.txt", @"Kısayol - \1", TransferKind.Shortcut));
        Assert.Equal("Kısayol - a.lnk", items[0].NewName);
        var result = RenameExecutor.Execute(items);

        Assert.Empty(result.Failures);
        string lnk = Path.Combine(tmp.Root, "Kısayol - a.lnk");
        Assert.True(File.Exists(lnk));
        Assert.Contains(Path.GetFileName(target), System.Text.Encoding.Unicode.GetString(File.ReadAllBytes(lnk)) + System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(lnk)));

        RenameExecutor.Undo(result.Journal);

        Assert.Equal(["a.txt"], tmp.Tree());
    }

    [Fact]
    public void HardLink_SharesTheContent_AndUndoOnlyRemovesTheLink()
    {
        using var tmp = new TempFolder();
        tmp.File("a.txt", "hello");

        var result = RenameExecutor.Execute(Plan(tmp, Rule("*.*", @"bag_\1.\2", TransferKind.Hardlink)));

        Assert.Empty(result.Failures);
        File.AppendAllText(Path.Combine(tmp.Root, "a.txt"), "!");
        Assert.Equal("hello!", File.ReadAllText(Path.Combine(tmp.Root, "bag_a.txt")));

        RenameExecutor.Undo(result.Journal);

        Assert.Equal(["a.txt"], tmp.Tree());
        Assert.Equal("hello!", File.ReadAllText(Path.Combine(tmp.Root, "a.txt")));
    }

    [Fact]
    public void HardLink_OfAFolder_IsNotPossible()
    {
        using var tmp = new TempFolder();
        tmp.Dir("Klasör");

        var items = Plan(tmp, Rule("*", @"bag_\1", TransferKind.Hardlink), new ScanOptions { IncludeFiles = false, IncludeFolders = true });

        Assert.Equal(RenameStatus.InvalidName, items[0].Status);
    }

    // ---- History and recovery --------------------------------------------------------------------------------------

    [Fact]
    public void CopiesAreInTheHistory_AndSurviveARestart()
    {
        using var tmp = new TempFolder();
        string history = Path.Combine(tmp.Root, "..", "h-" + Guid.NewGuid().ToString("N") + ".json");
        tmp.File("a.txt", "hello");

        try
        {
            var service = new RenameService(new HistoryStore(history));
            service.Execute(Plan(tmp, Rule("*.*", @"Kopya_\1.\2", TransferKind.Copy)), "kopya");

            var restarted = new RenameService(new HistoryStore(history));
            Assert.True(restarted.CanUndo);
            var undone = restarted.UndoLatest();

            Assert.NotNull(undone);
            Assert.Empty(undone!.Value.Result.Failures);
            Assert.Equal(["a.txt"], tmp.Tree());
            Assert.False(restarted.CanUndo);
        }
        finally
        {
            File.Delete(history);
        }
    }

    [Fact]
    public void AnInterruptedCopy_LeavesNoHalfFile_AndIsNotCountedAsDone()
    {
        using var tmp = new TempFolder();
        string history = Path.Combine(tmp.Root, "..", "h-" + Guid.NewGuid().ToString("N") + ".json");
        tmp.File("a.txt", "hello");

        try
        {
            var store = new HistoryStore(history);
            var items = Plan(tmp, Rule("*.*", @"Kopya_\1.\2", TransferKind.Copy));
            var entry = store.Begin("kopya", items.Select(i => new RenameMove(i.Entry.Directory, i.Entry.Name, i.NewName, false, null, TransferKind.Copy)));

            // The program stopped while the copy was still under its temporary name.
            string temp = Path.Combine(tmp.Root, RenameExecutor.TempName(entry.Id.ToString("N"), "Kopya_a.txt"));
            File.WriteAllText(temp, "hel");

            var reports = new RenameService(new HistoryStore(history)).RecoverInterrupted();

            Assert.Single(reports);
            Assert.Equal(0, reports[0].Done);
            Assert.Equal(["a.txt"], tmp.Tree());
        }
        finally
        {
            File.Delete(history);
        }
    }
}
