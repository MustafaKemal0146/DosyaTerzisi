using DosyaTerzisi.Core;

namespace DosyaTerzisi.Tests;

public class NumberedObjectTests
{
    private static NumberedObjectPlan Plan(TempFolder tmp, NumberedObjectOptions o, Func<string, long?>? free = null)
    {
        o.Folder = tmp.Root;
        return NumberedObjectPlanner.Build(o, freeSpace: free ?? (_ => null));
    }

    private static string[] Names(NumberedObjectPlan plan) => plan.Items.Select(i => i.Name).ToArray();

    // ---- Sizes ----------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("0", 0)]
    [InlineData("512", 512)]
    [InlineData("10K", 10_240)]
    [InlineData("10 kb", 10_240)]
    [InlineData("1.5M", 1_572_864)]
    [InlineData("1,5m", 1_572_864)]
    [InlineData("2G", 2_147_483_648)]
    [InlineData("1T", 1_099_511_627_776)]
    public void SizeParser_ReadsUnits(string text, long expected)
    {
        Assert.True(SizeParser.TryParse(text, out long bytes));
        Assert.Equal(expected, bytes);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("-5")]
    [InlineData("K")]
    [InlineData("99999999999999T")]
    public void SizeParser_RejectsNonsense(string text) => Assert.False(SizeParser.TryParse(text, out _));

    // ---- Names ----------------------------------------------------------------------------------------------

    [Fact]
    public void QuestionMarks_BecomeThePaddedNumber()
    {
        using var tmp = new TempFolder();

        var plan = Plan(tmp, new NumberedObjectOptions { NamePattern = "Foto_???.jpg", Amount = 3, Start = 8, Step = 1 });

        Assert.Null(plan.Problem);
        Assert.Equal(["Foto_008.jpg", "Foto_009.jpg", "Foto_010.jpg"], Names(plan));
    }

    [Fact]
    public void StartAndStep_Apply()
    {
        using var tmp = new TempFolder();

        var plan = Plan(tmp, new NumberedObjectOptions { NamePattern = "ders ?", Amount = 3, Start = 5, Step = 5 });

        Assert.Equal(["ders 5", "ders 10", "ders 15"], Names(plan));
    }

    [Fact]
    public void WithoutAMarker_TheNumberGoesBeforeTheExtension()
    {
        using var tmp = new TempFolder();

        var files = Plan(tmp, new NumberedObjectOptions { NamePattern = "not.txt", Amount = 10, Start = 1 });
        var folders = Plan(tmp, new NumberedObjectOptions { Kind = ObjectKind.EmptyFolder, NamePattern = "hafta", Amount = 2 });

        Assert.Equal("not01.txt", Names(files)[0]);
        Assert.Equal("not10.txt", Names(files)[9]);
        Assert.Equal(["hafta1", "hafta2"], Names(folders));
    }

    [Fact]
    public void ASingleItem_KeepsTheNameAsTyped()
    {
        using var tmp = new TempFolder();

        var plan = Plan(tmp, new NumberedObjectOptions { NamePattern = "Notlar.txt", Amount = 1 });

        Assert.Equal(["Notlar.txt"], Names(plan));
    }

    [Fact]
    public void ExistingItems_AreMarked_AndNeverPlannedForOverwrite()
    {
        using var tmp = new TempFolder();
        tmp.File("x2.txt");
        tmp.Dir("x3.txt");

        var plan = Plan(tmp, new NumberedObjectOptions { NamePattern = "x?.txt", Amount = 3 });

        Assert.Equal([ObjectStatus.Ready, ObjectStatus.Exists, ObjectStatus.Exists], plan.Items.Select(i => i.Status));
    }

    [Fact]
    public void IllegalNames_AreMarked()
    {
        using var tmp = new TempFolder();

        var plan = Plan(tmp, new NumberedObjectOptions { NamePattern = "a/b ?", Amount = 1 });

        Assert.Equal(ObjectStatus.InvalidName, plan.Items.Single().Status);
        Assert.False(plan.CanCreate);
    }

    // ---- Refusals -------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(0, 1, 1)]
    [InlineData(NumberedObjectPlanner.MaxAmount + 1, 1, 1)]
    [InlineData(3, -1, 1)]
    [InlineData(3, 1, 0)]
    public void BadNumbers_GiveAProblem_AndNoItems(int amount, int start, int step)
    {
        using var tmp = new TempFolder();

        var plan = Plan(tmp, new NumberedObjectOptions { Amount = amount, Start = start, Step = step });

        Assert.NotNull(plan.Problem);
        Assert.Empty(plan.Items);
        Assert.False(plan.CanCreate);
    }

    [Fact]
    public void MissingTargetFolder_OrSource_IsAProblem()
    {
        using var tmp = new TempFolder();

        var noFolder = NumberedObjectPlanner.Build(new NumberedObjectOptions { Folder = Path.Combine(tmp.Root, "yok") });
        var noSource = Plan(tmp, new NumberedObjectOptions { Kind = ObjectKind.CopyOfFile, SourcePath = Path.Combine(tmp.Root, "yok.txt") });

        Assert.NotNull(noFolder.Problem);
        Assert.NotNull(noSource.Problem);
    }

    [Fact]
    public void ACopyOfAFolder_CannotGoIntoItself()
    {
        using var tmp = new TempFolder();
        tmp.Dir("kaynak");

        var plan = NumberedObjectPlanner.Build(new NumberedObjectOptions
        {
            Kind = ObjectKind.CopyOfFolder,
            SourcePath = Path.Combine(tmp.Root, "kaynak"),
            Folder = Path.Combine(tmp.Root, "kaynak"),
        });

        Assert.Contains("kendi içine", plan.Problem);
    }

    [Fact]
    public void NotEnoughDiskSpace_StopsThePlan()
    {
        using var tmp = new TempFolder();

        var plan = Plan(tmp, new NumberedObjectOptions { NamePattern = "b?", Amount = 3, SizeBytes = 1000 }, free: _ => 2999);
        var fits = Plan(tmp, new NumberedObjectOptions { NamePattern = "b?", Amount = 3, SizeBytes = 1000 }, free: _ => 3000);

        Assert.Contains("yeterli yer yok", plan.Problem);
        Assert.False(plan.CanCreate);
        Assert.Null(fits.Problem);
    }

    // ---- Creating -------------------------------------------------------------------------------------------

    [Fact]
    public void EmptyFiles_AreCreatedWithTheGivenSize()
    {
        using var tmp = new TempFolder();
        var options = new NumberedObjectOptions { NamePattern = "veri??.bin", Amount = 3, SizeBytes = 2048 };
        var plan = Plan(tmp, options);

        var result = NumberedObjectCreator.Create(options, plan);

        Assert.Empty(result.Failures);
        Assert.Equal(["veri01.bin", "veri02.bin", "veri03.bin"], tmp.Tree());
        Assert.All(result.Created, p => Assert.Equal(2048, new FileInfo(p).Length));
    }

    [Fact]
    public void EmptyFolders_AreCreated()
    {
        using var tmp = new TempFolder();
        var options = new NumberedObjectOptions { Kind = ObjectKind.EmptyFolder, NamePattern = "Hafta ??", Amount = 3 };

        var result = NumberedObjectCreator.Create(options, Plan(tmp, options));

        Assert.Equal(3, result.Created.Count);
        Assert.Equal(["Hafta 01", "Hafta 02", "Hafta 03"], tmp.Tree());
    }

    [Fact]
    public void CopiesOfAFile_KeepTheContent()
    {
        using var tmp = new TempFolder();
        var source = tmp.File(@"kaynak\sablon.docx", "şablon içeriği");
        var options = new NumberedObjectOptions
        {
            Kind = ObjectKind.CopyOfFile,
            SourcePath = source,
            NamePattern = "Ödev ?.docx",
            Amount = 2,
        };

        NumberedObjectCreator.Create(options, Plan(tmp, options));

        Assert.Equal("şablon içeriği", File.ReadAllText(Path.Combine(tmp.Root, "Ödev 1.docx")));
        Assert.Equal("şablon içeriği", File.ReadAllText(Path.Combine(tmp.Root, "Ödev 2.docx")));
    }

    [Fact]
    public void CopiesOfAFolder_IncludeSubfolders()
    {
        using var tmp = new TempFolder();
        tmp.File(@"kaynak\a.txt", "a");
        tmp.File(@"kaynak\alt\b.txt", "b");
        var options = new NumberedObjectOptions
        {
            Kind = ObjectKind.CopyOfFolder,
            SourcePath = Path.Combine(tmp.Root, "kaynak"),
            NamePattern = "kopya ?",
            Amount = 2,
        };

        var result = NumberedObjectCreator.Create(options, Plan(tmp, options));

        Assert.Empty(result.Failures);
        Assert.Equal("b", File.ReadAllText(Path.Combine(tmp.Root, "kopya 2", "alt", "b.txt")));
        Assert.Equal("a", File.ReadAllText(Path.Combine(tmp.Root, "kopya 1", "a.txt")));
    }

    [Fact]
    public void ExistingFiles_AreNeverOverwritten()
    {
        using var tmp = new TempFolder();
        var existing = tmp.File("x2.txt", "dokunma");
        var options = new NumberedObjectOptions { NamePattern = "x?.txt", Amount = 3, SizeBytes = 10 };

        var result = NumberedObjectCreator.Create(options, Plan(tmp, options));

        Assert.Equal(2, result.Created.Count);
        Assert.Equal("dokunma", File.ReadAllText(existing));
    }

    [Fact]
    public void AFileThatAppearsAfterThePlan_IsReported_NotReplaced()
    {
        using var tmp = new TempFolder();
        var options = new NumberedObjectOptions { NamePattern = "y?.txt", Amount = 2, SizeBytes = 5 };
        var plan = Plan(tmp, options);
        var late = tmp.File("y1.txt", "geç geldi");

        var result = NumberedObjectCreator.Create(options, plan);

        Assert.Single(result.Failures);
        Assert.Equal(["y2.txt"], result.Created.Select(Path.GetFileName));
        Assert.Equal("geç geldi", File.ReadAllText(late));
    }

    [Fact]
    public void APlanWithAProblem_CreatesNothing()
    {
        using var tmp = new TempFolder();
        var options = new NumberedObjectOptions { NamePattern = "z?", Amount = 0 };

        var result = NumberedObjectCreator.Create(options, Plan(tmp, options));

        Assert.Empty(result.Created);
        Assert.Empty(tmp.Tree());
    }
}
