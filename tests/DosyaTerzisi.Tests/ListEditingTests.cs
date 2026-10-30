using DosyaTerzisi.Bridge.Contract;

namespace DosyaTerzisi.Tests;

/// <summary>Listede elle ad yazma (F2) ve satırları listeden çıkarma.</summary>
public class ListEditingTests
{
    [Fact]
    public async Task TypedName_ReplacesTheRuleResult_ForThatRowOnly()
    {
        using var h = new BridgeHarness();
        h.Files.File("a.txt");
        h.Files.File("b.txt");
        await h.Scan();
        var rule = BridgeHarness.Rule(RenameMethod.Wildcard, "*.*", @"x_\1.\2");

        var preview = await h.PreviewWith(rule, [new NameOverride(1, "Özel ad.txt")]);

        Assert.Equal(["x_a.txt", "Özel ad.txt"], preview.Rows.Select(r => r.NewName));
        await h.Rename();
        Assert.Equal(
            ["x_a.txt", "Özel ad.txt"],
            Directory.GetFiles(h.Root).Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal));
    }

    [Fact]
    public async Task TypedName_IsStillChecked_AndConflictsAreReported()
    {
        using var h = new BridgeHarness();
        h.Files.File("a.txt");
        h.Files.File("b.txt");
        await h.Scan();
        var rule = BridgeHarness.Rule(RenameMethod.Wildcard, "*.*", @"\1.\2");

        var bad = await h.PreviewWith(rule, [new NameOverride(0, "a:b.txt")]);
        var clash = await h.PreviewWith(rule, [new NameOverride(0, "b.txt")]);

        Assert.Equal(RowStatus.Invalid, bad.Rows[0].Status);
        Assert.Equal(RowStatus.Conflict, clash.Rows[0].Status);
    }

    [Fact]
    public async Task RemoveRows_DropsThemFromTheList_WithoutTouchingFiles()
    {
        using var h = new BridgeHarness();
        h.Files.File("a.txt");
        h.Files.File("b.txt");
        h.Files.File("c.txt");
        await h.Scan();

        var left = await h.Call<ScanResult>("removeRows", new RemoveRowsParams([1]));

        Assert.Equal(["a.txt", "c.txt"], left.Rows.Select(r => r.Name));
        Assert.Equal([0, 1], left.Rows.Select(r => r.Id));
        Assert.True(File.Exists(Path.Combine(h.Root, "b.txt")));

        // Önizleme ve adlandırma yalnızca kalan satırlara uygulanır.
        var preview = await h.Preview(BridgeHarness.Rule(RenameMethod.Wildcard, "*.txt", @"n_\1.md"));
        Assert.Equal(2, preview.Rows.Count);
        await h.Rename();
        Assert.True(File.Exists(Path.Combine(h.Root, "b.txt")));
        Assert.False(File.Exists(Path.Combine(h.Root, "a.txt")));

        // Yenileyince klasördeki her şey geri gelir.
        var refreshed = await h.Call<ScanResult>("refresh", new Empty());
        Assert.Equal(3, refreshed.Rows.Count);
    }
}

public class BridgeTransferTests
{
    private static Rule CopyRule(string search, string replace, string target = "") =>
        BridgeHarness.Rule(RenameMethod.Wildcard, search, replace) with { Operation = Operation.Copy, TargetFolder = target };

    [Fact]
    public async Task Copy_GoesThroughTheBridge_ShowsTheDestination_AndCanBeUndone()
    {
        using var h = new BridgeHarness();
        h.Files.File("a.txt", "hello");
        string backup = h.Files.Dir("Yedek");
        await h.Scan(BridgeHarness.AllFiles with { IncludeFolders = false });

        var preview = await h.Preview(CopyRule("*.*", @"\1.\2", backup));

        Assert.Equal(RowStatus.Ready, preview.Rows[0].Status);
        Assert.Equal(backup, preview.Rows[0].Destination);

        var outcome = await h.Rename();

        Assert.Equal(1, outcome.Renamed);
        Assert.Equal("hello", File.ReadAllText(Path.Combine(backup, "a.txt")));
        Assert.True(File.Exists(Path.Combine(h.Root, "a.txt")));

        var undone = await h.Call<UndoOutcome?>("undo", new Empty());

        Assert.NotNull(undone);
        Assert.False(File.Exists(Path.Combine(backup, "a.txt")));
        Assert.True(File.Exists(Path.Combine(h.Root, "a.txt")));
    }

    [Fact]
    public async Task MoveWithAPath_ThroughTheBridge_FollowsTheFileInADroppedList()
    {
        using var h = new BridgeHarness();
        string a = h.Files.File("a.txt");
        await h.Call<OpenPathsResult>("openPaths", new OpenPathsParams([a, h.Files.File("b.txt")]));

        await h.Preview(BridgeHarness.Rule(RenameMethod.Wildcard, "*.*", @"Alt/\1.\2"));
        await h.Rename();

        var list = await h.Call<ScanResult>("refresh", new Empty());
        Assert.Equal(2, list.Rows.Count);
        Assert.All(list.Rows, r => Assert.EndsWith("Alt", r.Dir));
    }
}

public class ShellTagsOnRequestTests
{
    [Fact]
    public async Task ShellProperties_AreOnlyReadWhenAsked()
    {
        using var h = new BridgeHarness();
        h.Files.File("a.txt", "merhaba");
        await h.Scan();

        var quick = await h.Call<TagList>("readTags", new ReadTagsParams(0, false));
        var full = await h.Call<TagList>("readTags", new ReadTagsParams(0, true));

        Assert.DoesNotContain(quick.Entries, e => e.Name.StartsWith("PROP.", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(full.Entries, e => e.Name.StartsWith("PROP.", StringComparison.OrdinalIgnoreCase));
    }
}
