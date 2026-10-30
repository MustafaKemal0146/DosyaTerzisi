using DosyaTerzisi.Core;

namespace DosyaTerzisi.Tests;

/// <summary>Builds a small folder tree in the temp directory:
/// <code>
/// root/  a.txt  B.JPG  c2.txt  c10.txt  .hidden.txt(Hidden)  sys.dat(System)
///   alpha/  x.txt  deep/  y.txt  deeper/  z.txt
///   beta/   w.png
/// </code></summary>
public sealed class ScanTree : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "dosyaterzisi-scan-" + Guid.NewGuid().ToString("N"));

    public ScanTree()
    {
        Make("a.txt", "B.JPG", "c2.txt", "c10.txt", ".hidden.txt", "sys.dat",
            @"alpha\x.txt", @"alpha\deep\y.txt", @"alpha\deep\deeper\z.txt", @"beta\w.png");

        System.IO.File.SetAttributes(Path.Combine(Root, ".hidden.txt"), FileAttributes.Hidden);
        System.IO.File.SetAttributes(Path.Combine(Root, "sys.dat"), FileAttributes.System);
    }

    private void Make(params string[] relative)
    {
        foreach (var rel in relative)
        {
            var full = Path.Combine(Root, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            System.IO.File.WriteAllText(full, new string('x', rel.Length));
        }
    }

    public void Dispose()
    {
        foreach (var path in Directory.EnumerateFileSystemEntries(Root, "*", SearchOption.AllDirectories))
            System.IO.File.SetAttributes(path, FileAttributes.Normal);
        Directory.Delete(Root, recursive: true);
    }
}

public class FileScannerTests : IClassFixture<ScanTree>
{
    private readonly ScanTree _tree;

    public FileScannerTests(ScanTree tree) => _tree = tree;

    private IEnumerable<string> Names(ScanOptions options)
    {
        var result = FileScanner.Scan(_tree.Root, options);
        Assert.Empty(result.Errors);
        return result.Entries.Select(e => Path.GetRelativePath(_tree.Root, e.FullPath));
    }

    [Fact]
    public void TopLevelFilesOnly_InNaturalOrder_WithoutSystemFiles()
    {
        Assert.Equal([".hidden.txt", "a.txt", "B.JPG", "c2.txt", "c10.txt"], Names(new ScanOptions()));
    }

    [Fact]
    public void FoldersOnly()
    {
        var options = new ScanOptions { IncludeFiles = false, IncludeFolders = true };

        Assert.Equal(["alpha", "beta"], Names(options));
    }

    [Fact]
    public void FoldersAndFiles_ListFoldersFirst()
    {
        var options = new ScanOptions { IncludeFolders = true, IncludeHidden = false };

        Assert.Equal(["alpha", "beta", "a.txt", "B.JPG", "c2.txt", "c10.txt"], Names(options));
    }

    [Fact]
    public void Depth1_EntersDirectSubfoldersOnly()
    {
        var options = new ScanOptions { MaxDepth = 1, Filter = "*.txt" };

        Assert.Equal(
            [".hidden.txt", "a.txt", "c2.txt", "c10.txt", @"alpha\x.txt"],
            Names(options));
    }

    [Fact]
    public void Depth2_ReachesDeepButNotDeeper()
    {
        var options = new ScanOptions { MaxDepth = 2, Filter = "*.txt", IncludeHidden = false };

        Assert.Equal(
            ["a.txt", "c2.txt", "c10.txt", @"alpha\x.txt", @"alpha\deep\y.txt"],
            Names(options));
    }

    [Fact]
    public void UnlimitedDepth_FindsEverything()
    {
        var options = new ScanOptions { MaxDepth = ScanOptions.Unlimited, Filter = "z.txt" };

        Assert.Equal([@"alpha\deep\deeper\z.txt"], Names(options));
    }

    [Fact]
    public void ParentIsListedBeforeItsSubfolderContents()
    {
        var options = new ScanOptions { MaxDepth = ScanOptions.Unlimited, IncludeFolders = true, Filter = "*" };
        var names = Names(options).ToList();

        Assert.True(names.IndexOf("alpha") < names.IndexOf(@"alpha\x.txt"));
        // Inside a folder the subfolders come first, then the files, then the subfolder contents.
        Assert.True(names.IndexOf(@"alpha\deep") < names.IndexOf(@"alpha\x.txt"));
        Assert.True(names.IndexOf(@"alpha\x.txt") < names.IndexOf(@"alpha\deep\y.txt"));
    }

    [Fact]
    public void Filter_MultiplePatterns_IgnoreCase()
    {
        var options = new ScanOptions { MaxDepth = ScanOptions.Unlimited, Filter = "*.jpg; *.PNG" };

        Assert.Equal(["B.JPG", @"beta\w.png"], Names(options));
    }

    [Fact]
    public void Filter_DoesNotStopTheWalkIntoNonMatchingFolders()
    {
        var options = new ScanOptions { MaxDepth = 1, IncludeFolders = true, Filter = "*.png" };

        Assert.Equal([@"beta\w.png"], Names(options));
    }

    [Fact]
    public void Filter_AppliesToFolderNamesToo()
    {
        var options = new ScanOptions { IncludeFiles = false, IncludeFolders = true, Filter = "al*" };

        Assert.Equal(["alpha"], Names(options));
    }

    [Fact]
    public void Hidden_CanBeExcluded()
    {
        var options = new ScanOptions { IncludeHidden = false };

        Assert.DoesNotContain(".hidden.txt", Names(options));
    }

    [Fact]
    public void System_CanBeIncluded()
    {
        var options = new ScanOptions { IncludeSystem = true };

        Assert.Contains("sys.dat", Names(options));
    }

    [Fact]
    public void Entries_CarrySizeAndDates()
    {
        var entry = FileScanner.Scan(_tree.Root, new ScanOptions { Filter = "a.txt" }).Entries.Single();

        Assert.Equal("a.txt".Length, entry.Size);
        Assert.Equal(_tree.Root, entry.Directory);
        Assert.True(entry.Modified > DateTime.Now.AddMinutes(-5));
    }

    [Fact]
    public void ScannedEntries_CanBePlanned()
    {
        var scan = FileScanner.Scan(_tree.Root, new ScanOptions { MaxDepth = 1, Filter = "*.txt", IncludeHidden = false });
        var rule = new RenameOptions { Method = RenameMethod.Wildcard, Search = "*.*", Replace = @"??_\1.\2" };

        var plan = RenamePlanner.Build(scan.Entries, rule);

        Assert.Equal(["01_a.txt", "02_c2.txt", "03_c10.txt", "04_x.txt"], plan.Select(i => i.NewName));
        Assert.All(plan, i => Assert.Equal(RenameStatus.Ready, i.Status));
    }

    [Fact]
    public void MissingRoot_Throws()
    {
        Assert.Throws<DirectoryNotFoundException>(
            () => FileScanner.Scan(Path.Combine(_tree.Root, "nope"), new ScanOptions()));
    }

    [Fact]
    public void CancelledToken_StopsTheScan()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(
            () => FileScanner.Scan(_tree.Root, new ScanOptions { MaxDepth = ScanOptions.Unlimited }, cts.Token));
    }

    [Fact]
    public void FromPaths_ReadsFilesAndFolders_AndReportsMissing()
    {
        var result = FileScanner.FromPaths(
        [
            Path.Combine(_tree.Root, "a.txt"),
            Path.Combine(_tree.Root, "alpha"),
            Path.Combine(_tree.Root, "missing.txt"),
        ]);

        Assert.Equal(["a.txt", "alpha"], result.Entries.Select(e => e.Name));
        Assert.True(result.Entries[1].IsDirectory);
        Assert.Equal("missing.txt", Path.GetFileName(result.Errors.Single().Path));
    }
}

public class ScanLinkTests
{
    [Fact]
    public void SymbolicLinkToParent_IsNotFollowed()
    {
        var root = Path.Combine(Path.GetTempPath(), "dosyaterzisi-link-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "real"));
        System.IO.File.WriteAllText(Path.Combine(root, "real", "f.txt"), "x");
        try
        {
            try
            {
                Directory.CreateSymbolicLink(Path.Combine(root, "real", "loop"), root);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return; // Windows without symlink permission: nothing to verify here.
            }

            var result = FileScanner.Scan(root, new ScanOptions { MaxDepth = ScanOptions.Unlimited, IncludeFolders = true });

            Assert.Contains(result.Entries, e => e.Name == "loop");
            Assert.Single(result.Entries, e => e.Name == "f.txt");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

public class NameFilterTests
{
    [Theory]
    [InlineData("", "anything.bin", true)]
    [InlineData("*", "anything.bin", true)]
    [InlineData("*.*", "README", true)]
    [InlineData("*.jpg", "a.JPG", true)]
    [InlineData("*.jpg", "a.jpeg", false)]
    [InlineData("*.jpg;*.png", "b.png", true)]
    [InlineData(" *.jpg ; *.png ", "b.png", true)]
    [InlineData("a?.txt", "ab.txt", true)]
    [InlineData("a?.txt", "abc.txt", false)]
    [InlineData("a.b", "aXb", false)]
    [InlineData("rapor*", "Rapor 2026.docx", true)]
    public void Matches(string filter, string name, bool expected) =>
        Assert.Equal(expected, NameFilter.Parse(filter).IsMatch(name));
}

public class NaturalComparerTests
{
    [Fact]
    public void SortsNumbersByValue_AndLettersWithoutCase()
    {
        string[] names = ["file10", "File2", "file1", "file01a", "b", "A"];

        var sorted = names.Order(NaturalComparer.Invariant).ToArray();

        Assert.Equal(["A", "b", "file1", "file01a", "File2", "file10"], sorted);
    }

    [Fact]
    public void ShorterNameComesFirst_WhenOneIsPrefix()
    {
        Assert.True(NaturalComparer.Invariant.Compare("abc", "abc1") < 0);
        Assert.Equal(0, NaturalComparer.Invariant.Compare("abc", "ABC"));
    }
}
