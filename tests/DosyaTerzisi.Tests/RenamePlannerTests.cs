using DosyaTerzisi.Core;
using static DosyaTerzisi.Tests.TestHelpers;

namespace DosyaTerzisi.Tests;

public class RenamePlannerTests
{
    private static RenameOptions ToConstant(string name) => Options(RenameMethod.Wildcard, "*", name);

    [Fact]
    public void SameName_IsUnchanged()
    {
        var item = RenamePlanner.Build([File("a.txt")], ToConstant("a.txt"), NoDisk).Single();

        Assert.Equal(RenameStatus.Unchanged, item.Status);
        Assert.False(item.WillRename);
    }

    [Fact]
    public void TwoItemsGettingTheSameName_SecondIsConflict()
    {
        var plan = RenamePlanner.Build([File("a.txt"), File("b.txt")], ToConstant("x.txt"), NoDisk);

        Assert.Equal(RenameStatus.Ready, plan[0].Status);
        Assert.Equal(RenameStatus.Conflict, plan[1].Status);
    }

    [Fact]
    public void SameNameInDifferentFolders_IsFine()
    {
        var plan = RenamePlanner.Build([File("a.txt", @"C:\x"), File("b.txt", @"C:\y")], ToConstant("x.txt"), NoDisk);

        Assert.All(plan, i => Assert.Equal(RenameStatus.Ready, i.Status));
    }

    [Fact]
    public void ExistingFileOnDisk_IsConflict()
    {
        var disk = new PlanOptions { ListDirectory = _ => ["a.txt", "taken.txt"] };

        var item = RenamePlanner.Build([File("a.txt")], ToConstant("taken.txt"), disk).Single();

        Assert.Equal(RenameStatus.Conflict, item.Status);
    }

    [Fact]
    public void CaseOnlyRename_IsNotAConflict()
    {
        var disk = new PlanOptions { ListDirectory = _ => ["a.txt"] };

        var item = RenamePlanner.Build([File("a.txt")], ToConstant("A.txt"), disk).Single();

        Assert.Equal(RenameStatus.Ready, item.Status);
    }

    [Fact]
    public void SwappingTwoNames_IsNotAConflict()
    {
        var o = Options(RenameMethod.Wildcard, "*-*.*", @"\2-\1.\3");
        var disk = new PlanOptions { ListDirectory = _ => ["a-b.txt", "b-a.txt"] };

        var plan = RenamePlanner.Build([File("a-b.txt"), File("b-a.txt")], o, disk);

        Assert.Equal(["b-a.txt", "a-b.txt"], plan.Select(i => i.NewName));
        Assert.All(plan, i => Assert.Equal(RenameStatus.Ready, i.Status));
    }

    [Fact]
    public void AutoNumber_AddsSuffixBeforeExtension()
    {
        var plan = RenamePlanner.Build(
            [File("a.txt"), File("b.txt"), File("c.txt")],
            ToConstant("x.txt"),
            new PlanOptions { Conflict = ConflictPolicy.AutoNumber, ListDirectory = _ => [] });

        Assert.Equal(["x.txt", "x(01).txt", "x(02).txt"], plan.Select(i => i.NewName));
        Assert.All(plan, i => Assert.Equal(RenameStatus.Ready, i.Status));
    }

    [Fact]
    public void AutoNumber_SkipsNamesAlreadyOnDisk()
    {
        var plan = RenamePlanner.Build(
            [File("a.txt")],
            ToConstant("x.txt"),
            new PlanOptions
            {
                Conflict = ConflictPolicy.AutoNumber,
                DuplicateFormat = "_?",
                ListDirectory = _ => ["x.txt", "x_1.txt"],
            });

        Assert.Equal("x_2.txt", plan[0].NewName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("a:b.txt")]
    [InlineData("what?.txt")]
    [InlineData("trailing.")]
    [InlineData("trailing ")]
    [InlineData("CON.txt")]
    [InlineData("nul")]
    public void IllegalNames_AreRejected(string name)
    {
        // A regex rule that replaces the whole name lets any text reach the validator.
        var o = Options(RenameMethod.RegularExpression, "^.*$", EscapeForTemplate(name));

        var item = RenamePlanner.Build([File("a.txt")], o, NoDisk).Single();

        Assert.Equal(RenameStatus.InvalidName, item.Status);
    }

    [Fact]
    public void LegalTurkishName_IsAccepted()
    {
        Assert.Null(NameValidator.Check("Öğrenci Şeması (GÜNCEL).docx"));
    }

    [Fact]
    public void BrokenRule_MarksEveryItemAsError()
    {
        var plan = RenamePlanner.Build(
            [File("a.txt"), File("b.txt")], Options(RenameMethod.RegularExpression, "[", ""), NoDisk);

        Assert.All(plan, i => Assert.Equal(RenameStatus.Error, i.Status));
    }

    [Fact]
    public void Planner_NeverTouchesTheDisk()
    {
        var path = Path.Combine(Path.GetTempPath(), "dosyaterzisi-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        try
        {
            var file = Path.Combine(path, "a.txt");
            System.IO.File.WriteAllText(file, "x");
            var entry = FileEntry.FromPath(file)!;

            var plan = RenamePlanner.Build([entry], ToConstant("b.txt"));

            Assert.Equal(RenameStatus.Ready, plan[0].Status);
            Assert.True(System.IO.File.Exists(file));
            Assert.False(System.IO.File.Exists(Path.Combine(path, "b.txt")));
        }
        finally
        {
            Directory.Delete(path, recursive: true);
        }
    }

    [Fact]
    public void FileEntry_FromPath_ReadsRealFileAndFolder()
    {
        var path = Path.Combine(Path.GetTempPath(), "dosyaterzisi-" + Guid.NewGuid().ToString("N"));
        var sub = Directory.CreateDirectory(Path.Combine(path, "alt.klasor"));
        try
        {
            var file = Path.Combine(path, "a.b.txt");
            System.IO.File.WriteAllText(file, "x");

            var f = FileEntry.FromPath(file)!;
            var d = FileEntry.FromPath(sub.FullName)!;

            Assert.False(f.IsDirectory);
            Assert.Equal("a.b", f.Title);
            Assert.Equal("txt", f.Extension);
            Assert.True(d.IsDirectory);
            Assert.Equal("alt.klasor", d.Title);
            Assert.Equal(Path.GetFileName(path), d.FolderName);
        }
        finally
        {
            Directory.Delete(path, recursive: true);
        }
    }

    /// <summary>Escapes template characters so the text is produced literally.</summary>
    private static string EscapeForTemplate(string text) =>
        string.Concat(text.Select(c => c is '\\' or '?' ? "\\" + c : c.ToString()));
}
