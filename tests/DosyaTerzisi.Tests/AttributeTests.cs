using DosyaTerzisi.Core;

namespace DosyaTerzisi.Tests;

public class AttributeTests
{
    private static readonly DateTime Jan1 = new(2020, 1, 1, 12, 0, 0);

    private static FileEntry Read(string path) => FileEntry.FromPath(path)!;

    private static IReadOnlyList<AttributeItem> Plan(TempFolder tmp, AttributeRule rule, ScanOptions? scan = null) =>
        AttributePlanner.Build(
            FileScanner.Scan(tmp.Root, scan ?? new ScanOptions { IncludeFolders = true, IncludeSystem = true }).Entries,
            rule);

    // ---- Planner (no disk changes) --------------------------------------------------------------------------

    [Fact]
    public void Planner_KeepsEverythingByDefault()
    {
        using var tmp = new TempFolder();
        tmp.File("a.txt");

        var item = Plan(tmp, new AttributeRule()).Single();

        Assert.Equal(AttributeStatus.Unchanged, item.Status);
        Assert.False(item.WillChange);
    }

    [Fact]
    public void Planner_SetsAndClearsOnlyTheRequestedFlags()
    {
        var entry = new FileEntry(@"C:\x", "a.txt", false, Jan1, Jan1) { Attributes = FileAttributes.Archive | FileAttributes.Hidden };
        var rule = new AttributeRule { ReadOnly = Tri.Set, Hidden = Tri.Clear, System = Tri.Keep };

        var item = AttributePlanner.Build([entry], rule).Single();

        Assert.Equal(FileAttributes.Archive | FileAttributes.ReadOnly, item.NewAttributes);
        Assert.True(item.ChangeAttributes);
        Assert.Equal(AttributeStatus.Ready, item.Status);
    }

    [Fact]
    public void Planner_NoFlagsLeft_BecomesNormal()
    {
        var entry = new FileEntry(@"C:\x", "a.txt", false, Jan1, Jan1) { Attributes = FileAttributes.Archive };

        var item = AttributePlanner.Build([entry], new AttributeRule { Archive = Tri.Clear }).Single();

        Assert.Equal(FileAttributes.Normal, item.NewAttributes);
    }

    [Fact]
    public void Planner_SettingAFlagThatIsAlreadySet_IsNotAChange()
    {
        var entry = new FileEntry(@"C:\x", "a.txt", false, Jan1, Jan1) { Attributes = FileAttributes.Archive };

        var item = AttributePlanner.Build([entry], new AttributeRule { Archive = Tri.Set }).Single();

        Assert.Equal(AttributeStatus.Unchanged, item.Status);
    }

    [Fact]
    public void Planner_TimeRules()
    {
        var entry = new FileEntry(@"C:\x", "a.txt", false, Jan1, Jan1.AddDays(-5)) { Accessed = Jan1.AddDays(3) };
        var rule = new AttributeRule
        {
            Modified = TimestampRule.ShiftBy(TimeSpan.FromDays(-2)),
            Created = TimestampRule.SetTo(new DateTime(2000, 6, 7, 8, 9, 10)),
            Accessed = TimestampRule.CopyOf(TimestampKind.Modified),
        };

        var item = AttributePlanner.Build([entry], rule).Single();

        Assert.Equal(Jan1.AddDays(-2), item.NewModified);
        Assert.Equal(new DateTime(2000, 6, 7, 8, 9, 10), item.NewCreated);
        Assert.Equal(Jan1, item.NewAccessed);
        Assert.True(item.ChangeModified && item.ChangeCreated && item.ChangeAccessed);
    }

    [Fact]
    public void Planner_CopyFrom_UsesTheTimesAsTheyWereBefore()
    {
        var entry = new FileEntry(@"C:\x", "a.txt", false, Jan1, Jan1.AddDays(10)) { Accessed = Jan1 };
        var rule = new AttributeRule
        {
            Modified = TimestampRule.CopyOf(TimestampKind.Created),
            Created = TimestampRule.CopyOf(TimestampKind.Modified),
        };

        var item = AttributePlanner.Build([entry], rule).Single();

        // Swapped, not both the same.
        Assert.Equal(Jan1.AddDays(10), item.NewModified);
        Assert.Equal(Jan1, item.NewCreated);
    }

    [Fact]
    public void Planner_ImpossibleTimes_AreErrors()
    {
        var entry = new FileEntry(@"C:\x", "a.txt", false, Jan1, Jan1);

        var early = AttributePlanner.Build([entry], new AttributeRule { Modified = TimestampRule.ShiftBy(TimeSpan.FromDays(-365_000)) }).Single();
        var zero = AttributePlanner.Build([entry], new AttributeRule { Created = TimestampRule.SetTo(DateTime.MinValue) }).Single();

        Assert.Equal(AttributeStatus.Error, early.Status);
        Assert.Equal(AttributeStatus.Error, zero.Status);
        Assert.NotNull(early.Message);
        Assert.False(early.WillChange);
    }

    [Fact]
    public void Planner_CanLeaveFoldersAlone()
    {
        using var tmp = new TempFolder();
        tmp.File("a.txt");
        tmp.Dir("klasor");

        var items = Plan(tmp, new AttributeRule { Hidden = Tri.Set, IncludeFolders = false });

        Assert.Equal(["a.txt"], items.Select(i => i.Entry.Name));
    }

    // ---- Disk -----------------------------------------------------------------------------------------------

    [Fact]
    public void Apply_ChangesAttributesOnDisk()
    {
        using var tmp = new TempFolder();
        var path = tmp.File("a.txt");

        var result = AttributeExecutor.Apply(Plan(tmp, new AttributeRule { ReadOnly = Tri.Set, Hidden = Tri.Set }));

        Assert.Empty(result.Failures);
        var attributes = File.GetAttributes(path);
        Assert.True(attributes.HasFlag(FileAttributes.ReadOnly));
        Assert.True(attributes.HasFlag(FileAttributes.Hidden));
    }

    [Fact]
    public void Apply_ChangesTimesOnDisk_ForFilesAndFolders()
    {
        using var tmp = new TempFolder();
        var file = tmp.File("a.txt");
        var folder = tmp.Dir("klasor");
        var rule = new AttributeRule
        {
            Modified = TimestampRule.SetTo(Jan1),
            Created = TimestampRule.SetTo(Jan1.AddDays(-30)),
            Accessed = TimestampRule.SetTo(Jan1.AddDays(1)),
        };

        var result = AttributeExecutor.Apply(Plan(tmp, rule));

        Assert.Empty(result.Failures);
        foreach (var path in new[] { file, folder })
        {
            var entry = Read(path);
            Assert.Equal(Jan1, entry.Modified);
            Assert.Equal(Jan1.AddDays(-30), entry.Created);
            Assert.Equal(Jan1.AddDays(1).Date, entry.Accessed.Date);
        }
    }

    [Fact]
    public void Apply_ReadOnlyFiles_CanStillGetNewTimes_AndStayReadOnly()
    {
        using var tmp = new TempFolder();
        var path = tmp.File("a.txt");
        File.SetAttributes(path, FileAttributes.ReadOnly);

        var result = AttributeExecutor.Apply(Plan(tmp, new AttributeRule { Modified = TimestampRule.SetTo(Jan1) }));

        Assert.Empty(result.Failures);
        Assert.Equal(Jan1, Read(path).Modified);
        Assert.True(File.GetAttributes(path).HasFlag(FileAttributes.ReadOnly));
    }

    [Fact]
    public void Apply_ClearingReadOnly_AndChangingTimeInOneGo()
    {
        using var tmp = new TempFolder();
        var path = tmp.File("a.txt");
        File.SetAttributes(path, FileAttributes.ReadOnly);

        var result = AttributeExecutor.Apply(
            Plan(tmp, new AttributeRule { ReadOnly = Tri.Clear, Modified = TimestampRule.SetTo(Jan1) }));

        Assert.Empty(result.Failures);
        Assert.False(File.GetAttributes(path).HasFlag(FileAttributes.ReadOnly));
        Assert.Equal(Jan1, Read(path).Modified);
    }

    [Fact]
    public void Apply_ShiftsEachFileFromItsOwnTime()
    {
        using var tmp = new TempFolder();
        var a = tmp.File("a.txt");
        var b = tmp.File("b.txt");
        File.SetLastWriteTime(a, Jan1);
        File.SetLastWriteTime(b, Jan1.AddDays(10));

        AttributeExecutor.Apply(Plan(tmp, new AttributeRule { Modified = TimestampRule.ShiftBy(TimeSpan.FromHours(36)) }));

        Assert.Equal(Jan1.AddHours(36), Read(a).Modified);
        Assert.Equal(Jan1.AddDays(10).AddHours(36), Read(b).Modified);
    }

    [Fact]
    public void Undo_RestoresAttributesAndTimes()
    {
        using var tmp = new TempFolder();
        var path = tmp.File("a.txt");
        File.SetLastWriteTime(path, Jan1);
        File.SetCreationTime(path, Jan1.AddDays(-1));
        File.SetAttributes(path, FileAttributes.ReadOnly | FileAttributes.Archive);
        var before = Read(path);

        var rule = new AttributeRule
        {
            ReadOnly = Tri.Clear,
            Hidden = Tri.Set,
            Modified = TimestampRule.SetTo(Jan1.AddYears(5)),
            Created = TimestampRule.SetTo(Jan1.AddYears(4)),
        };
        var applied = AttributeExecutor.Apply(Plan(tmp, rule));
        Assert.Equal(Jan1.AddYears(5), Read(path).Modified);

        var undo = AttributeExecutor.Undo(applied.Journal);

        Assert.Empty(undo.Failures);
        var after = Read(path);
        Assert.Equal(before.Modified, after.Modified);
        Assert.Equal(before.Created, after.Created);
        Assert.Equal(before.Attributes, after.Attributes);
    }

    [Fact]
    public void Undo_TakesFoldersAndFilesBack_InReverseOrder()
    {
        using var tmp = new TempFolder();
        tmp.File(@"klasor\a.txt");
        var scan = new ScanOptions { IncludeFolders = true, MaxDepth = ScanOptions.Unlimited };
        var rule = new AttributeRule { Hidden = Tri.Set };

        var applied = AttributeExecutor.Apply(Plan(tmp, rule, scan));
        Assert.Equal(2, applied.Journal.Items.Count);

        var undo = AttributeExecutor.Undo(applied.Journal);

        Assert.Empty(undo.Failures);
        Assert.All(
            new[] { Path.Combine(tmp.Root, "klasor"), Path.Combine(tmp.Root, "klasor", "a.txt") },
            p => Assert.False(File.GetAttributes(p).HasFlag(FileAttributes.Hidden)));
    }

    [Fact]
    public void FileThatDisappeared_IsReported_AndOthersStillChange()
    {
        using var tmp = new TempFolder();
        tmp.File("a.txt");
        var b = tmp.File("b.txt");
        var items = Plan(tmp, new AttributeRule { Hidden = Tri.Set });
        File.Delete(Path.Combine(tmp.Root, "a.txt"));

        var result = AttributeExecutor.Apply(items);

        Assert.Single(result.Failures);
        Assert.EndsWith("a.txt", result.Failures[0].Path);
        Assert.True(File.GetAttributes(b).HasFlag(FileAttributes.Hidden));
        Assert.Single(result.Journal.Items);
    }

    [Fact]
    public void ItemsThatNeedNoChange_AreNotTouched()
    {
        using var tmp = new TempFolder();
        var path = tmp.File("a.txt");
        File.SetLastWriteTime(path, Jan1);

        var result = AttributeExecutor.Apply(Plan(tmp, new AttributeRule { Modified = TimestampRule.SetTo(Jan1) }));

        Assert.Empty(result.Journal.Items);
        Assert.Empty(result.Failures);
    }
}

public class RandomAndLimitTimeTests
{
    private static readonly DateTime From = new(2024, 1, 1);
    private static readonly DateTime To = new(2024, 12, 31, 23, 59, 59);

    private static FileEntry File(string name) => new(@"C:\x", name, false, new DateTime(2020, 5, 5), new DateTime(2020, 5, 5));

    private static IReadOnlyList<AttributeItem> Plan(AttributeRule rule, params FileEntry[] entries) => AttributePlanner.Build(entries, rule);

    [Fact]
    public void Random_StaysInsideTheRange_AndDiffersBetweenFiles()
    {
        var rule = new AttributeRule { Modified = TimestampRule.RandomBetween(From, To, seed: 1) };

        var items = Plan(rule, Enumerable.Range(0, 40).Select(i => File($"f{i}.txt")).ToArray());

        Assert.All(items, i => Assert.InRange(i.NewModified, From, To));
        Assert.True(items.Select(i => i.NewModified).Distinct().Count() > 30);
    }

    [Fact]
    public void Random_IsRepeatable_SoThePreviewMatchesWhatIsApplied()
    {
        var rule = new AttributeRule { Modified = TimestampRule.RandomBetween(From, To, seed: 5) };

        var first = Plan(rule, File("a.txt")).Single().NewModified;
        var second = Plan(rule, File("a.txt")).Single().NewModified;
        var reshuffled = Plan(new AttributeRule { Modified = TimestampRule.RandomBetween(From, To, seed: 6) }, File("a.txt")).Single().NewModified;

        Assert.Equal(first, second);
        Assert.NotEqual(first, reshuffled);
    }

    [Fact]
    public void Random_AcceptsTheRangeInEitherOrder()
    {
        var rule = new AttributeRule { Modified = TimestampRule.RandomBetween(To, From, seed: 2) };

        Assert.InRange(Plan(rule, File("a.txt")).Single().NewModified, From, To);
    }

    [Fact]
    public void Limits_HoldTheResultInside_ForEveryAction()
    {
        var shift = TimestampRule.ShiftBy(TimeSpan.FromDays(3650));
        shift.Max = new DateTime(2025, 1, 1);
        var back = TimestampRule.ShiftBy(TimeSpan.FromDays(-3650));
        back.Min = new DateTime(2019, 1, 1);

        var later = Plan(new AttributeRule { Modified = shift }, File("a.txt")).Single();
        var earlier = Plan(new AttributeRule { Modified = back }, File("a.txt")).Single();

        Assert.Equal(new DateTime(2025, 1, 1), later.NewModified);
        Assert.Equal(new DateTime(2019, 1, 1), earlier.NewModified);
    }

    [Fact]
    public void Limits_DoNothingWhenTheActionIsKeep()
    {
        var rule = new AttributeRule { Modified = new TimestampRule { Min = new DateTime(2030, 1, 1) } };

        Assert.Equal(AttributeStatus.Unchanged, Plan(rule, File("a.txt")).Single().Status);
    }
}
