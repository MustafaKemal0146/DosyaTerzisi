using DosyaTerzisi.Core;

namespace DosyaTerzisi.Tests;

public sealed class TempFolder : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "dosyaterzisi-exec-" + Guid.NewGuid().ToString("N"));

    public TempFolder() => Directory.CreateDirectory(Root);

    public string File(string relative, string content = "x")
    {
        var full = Path.Combine(Root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        System.IO.File.WriteAllText(full, content);
        return full;
    }

    public string Dir(string relative)
    {
        var full = Path.Combine(Root, relative);
        Directory.CreateDirectory(full);
        return full;
    }

    /// <summary>Every file and folder below the root, relative, sorted.</summary>
    public string[] Tree() => Directory
        .EnumerateFileSystemEntries(Root, "*", SearchOption.AllDirectories)
        .Select(p => Path.GetRelativePath(Root, p))
        .Order(StringComparer.Ordinal)
        .ToArray();

    public void Dispose()
    {
        // Read-only files cannot be deleted; tests that set attributes must not leave the folder behind.
        foreach (var path in Directory.EnumerateFileSystemEntries(Root, "*", SearchOption.AllDirectories))
            System.IO.File.SetAttributes(path, FileAttributes.Normal);
        Directory.Delete(Root, recursive: true);
    }
}

public class RenameExecutorTests
{
    private static ExecutionResult Rename(TempFolder tmp, RenameOptions options, ScanOptions? scan = null, PlanOptions? plan = null)
    {
        var entries = FileScanner.Scan(tmp.Root, scan ?? new ScanOptions()).Entries;
        return RenameExecutor.Execute(RenamePlanner.Build(entries, options, plan));
    }

    private static RenameOptions Wildcard(string search, string replace) =>
        new() { Method = RenameMethod.Wildcard, Search = search, Replace = replace };

    [Fact]
    public void RenamesFilesOnDisk_AndKeepsContent()
    {
        using var tmp = new TempFolder();
        tmp.File("a - b.txt", "hello");

        var result = Rename(tmp, Wildcard("* - *", @"\1-\2"));

        Assert.Empty(result.Failures);
        Assert.Equal(["a-b.txt"], tmp.Tree());
        Assert.Equal("hello", System.IO.File.ReadAllText(Path.Combine(tmp.Root, "a-b.txt")));
    }

    [Fact]
    public void Undo_RestoresOriginalNames()
    {
        using var tmp = new TempFolder();
        tmp.File("a.txt");
        tmp.File("b.txt");

        var result = Rename(tmp, Wildcard("*.*", @"new_\1.\2"));
        Assert.Equal(["new_a.txt", "new_b.txt"], tmp.Tree());

        var undo = RenameExecutor.Undo(result.Journal);

        Assert.Empty(undo.Failures);
        Assert.Equal(["a.txt", "b.txt"], tmp.Tree());
    }

    [Fact]
    public void Swap_WorksThroughTemporaryNames()
    {
        using var tmp = new TempFolder();
        tmp.File("a-b.txt", "first");
        tmp.File("b-a.txt", "second");

        var result = Rename(tmp, Wildcard("*-*.*", @"\2-\1.\3"));

        Assert.Empty(result.Failures);
        Assert.Equal("second", System.IO.File.ReadAllText(Path.Combine(tmp.Root, "a-b.txt")));
        Assert.Equal("first", System.IO.File.ReadAllText(Path.Combine(tmp.Root, "b-a.txt")));
        Assert.Equal(["a-b.txt", "b-a.txt"], tmp.Tree());
    }

    [Fact]
    public void Chain_ShiftsNamesWithoutLosingFiles()
    {
        using var tmp = new TempFolder();
        tmp.File("1.txt", "one");
        tmp.File("2.txt", "two");
        tmp.File("3.txt", "three");

        // 1→2, 2→3, 3→4
        var options = Wildcard("*.*", @"?.\2");
        options.NumberStart = 2;

        var result = Rename(tmp, options);

        Assert.Empty(result.Failures);
        Assert.Equal(["2.txt", "3.txt", "4.txt"], tmp.Tree());
        Assert.Equal("one", System.IO.File.ReadAllText(Path.Combine(tmp.Root, "2.txt")));
        Assert.Equal("three", System.IO.File.ReadAllText(Path.Combine(tmp.Root, "4.txt")));
    }

    [Fact]
    public void CaseOnlyChange_WorksForFilesAndFolders()
    {
        using var tmp = new TempFolder();
        tmp.File("abc.txt");
        tmp.Dir("klasor");

        var options = new RenameOptions { Method = RenameMethod.Translation, Search = "a-z", Replace = "A-Z" };
        var scan = new ScanOptions { IncludeFolders = true };

        var result = Rename(tmp, options, scan);

        Assert.Empty(result.Failures);
        Assert.Equal(["ABC.txt", "KLASOR"], tmp.Tree());
    }

    [Fact]
    public void FolderAndItsContents_AreRenamedTogether_AndUndone()
    {
        using var tmp = new TempFolder();
        tmp.File(@"Eski Klasör\Şu Dosya.txt");
        tmp.File(@"Eski Klasör\iç\Öteki.txt");

        var options = new RenameOptions
        {
            Method = RenameMethod.Translation,
            Search = "ÜüĞğİıŞşÇçÖö ",
            Replace = "UuGgIiSsCcOo_",
        };
        var scan = new ScanOptions { IncludeFolders = true, MaxDepth = ScanOptions.Unlimited };

        var result = Rename(tmp, options, scan);

        Assert.Empty(result.Failures);
        Assert.Equal(
            [@"Eski_Klasor", @"Eski_Klasor\Su_Dosya.txt", @"Eski_Klasor\ic", @"Eski_Klasor\ic\Oteki.txt"],
            tmp.Tree());

        var undo = RenameExecutor.Undo(result.Journal);

        Assert.Empty(undo.Failures);
        Assert.Equal(
            [@"Eski Klasör", @"Eski Klasör\iç", @"Eski Klasör\iç\Öteki.txt", @"Eski Klasör\Şu Dosya.txt"],
            tmp.Tree());
    }

    [Fact]
    public void NeverOverwritesAnExistingFile()
    {
        using var tmp = new TempFolder();
        var a = tmp.File("a.txt", "a");
        tmp.File("taken.txt", "keep me");
        var entry = FileEntry.FromPath(a)!;

        // Bypass the planner (which would flag the conflict) to prove the executor itself is safe.
        var item = new RenameItem(entry, "taken.txt", RenameStatus.Ready);

        var result = RenameExecutor.Execute([item]);

        Assert.Single(result.Failures);
        Assert.Empty(result.Journal.Moves);
        Assert.Equal("keep me", System.IO.File.ReadAllText(Path.Combine(tmp.Root, "taken.txt")));
        Assert.Equal("a", System.IO.File.ReadAllText(a));
    }

    [Fact]
    public void LockedFile_IsReported_AndOthersStillRenamed()
    {
        using var tmp = new TempFolder();
        var locked = tmp.File("a.txt");
        tmp.File("b.txt");

        using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var result = Rename(tmp, Wildcard("*.*", @"new_\1.\2"));

            Assert.Single(result.Failures);
            Assert.EndsWith("a.txt", result.Failures[0].Path);
            Assert.Equal(["new_b.txt"], result.Journal.Moves.Select(m => m.NewName));
        }

        Assert.Equal(["a.txt", "new_b.txt"], tmp.Tree());
    }

    [Fact]
    public void OnlyReadyItemsAreExecuted()
    {
        using var tmp = new TempFolder();
        var path = tmp.File("a.txt");
        var entry = FileEntry.FromPath(path)!;

        var result = RenameExecutor.Execute(
        [
            new RenameItem(entry, "b.txt", RenameStatus.Conflict),
            new RenameItem(entry, "c.txt", RenameStatus.InvalidName),
            new RenameItem(entry, "a.txt", RenameStatus.Unchanged),
        ]);

        Assert.Empty(result.Journal.Moves);
        Assert.Equal(["a.txt"], tmp.Tree());
    }
}
