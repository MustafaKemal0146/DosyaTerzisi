using System.Globalization;
using DosyaTerzisi.Core;
using static DosyaTerzisi.Tests.TestHelpers;

namespace DosyaTerzisi.Tests;

public class RenameRuleTests
{
    // ---- Wildcard -------------------------------------------------------------------------------------------

    [Fact]
    public void Wildcard_ReplacesSpacedDash_LikeHocaLog()
    {
        var o = Options(RenameMethod.Wildcard, "* - *", @"\1-\2");

        Assert.Equal(
            "Ek I-4.9.1-Karar Alma Süreçleri ve Organizasyon Şeması",
            Rename(Folder("Ek I-4.9.1 - Karar Alma Süreçleri ve Organizasyon Şeması"), o));
    }

    [Fact]
    public void Wildcard_NoMatch_LeavesNameAlone()
    {
        var o = Options(RenameMethod.Wildcard, "* - *", @"\1-\2");

        Assert.Equal("rapor.docx", Rename(File("rapor.docx"), o));
    }

    [Fact]
    public void Wildcard_SwapBothSidesOfDash()
    {
        var o = Options(RenameMethod.Wildcard, "*-*.*", @"\2-\1.\3");

        Assert.Equal("b-a.txt", Rename(File("a-b.txt"), o));
    }

    [Fact]
    public void Wildcard_IsCaseInsensitiveByDefault()
    {
        var o = Options(RenameMethod.Wildcard, "*DSC*", @"\1IMG\2");

        Assert.Equal("IMG0001.jpg", Rename(File("dsc0001.jpg"), o));
        o.CaseSensitive = true;
        Assert.Equal("dsc0001.jpg", Rename(File("dsc0001.jpg"), o));
    }

    [Fact]
    public void Wildcard_QuestionMarkMatchesOneCharacterAndIsAGroup()
    {
        var o = Options(RenameMethod.Wildcard, "??-*", @"\3_\1\2");

        Assert.Equal("c_ab", Rename(File("ab-c"), o));
    }

    [Fact]
    public void Wildcard_DotsInPatternAreLiteral()
    {
        var o = Options(RenameMethod.Wildcard, "a.b", "x");

        Assert.Equal("aXb", Rename(File("aXb"), o));
    }

    // ---- Counters and variables -----------------------------------------------------------------------------

    [Fact]
    public void Counter_PadsToNumberOfQuestionMarks()
    {
        var o = Options(RenameMethod.Wildcard, "*.*", @"???_\1.\2");
        o.NumberStart = 5;
        o.NumberStep = 5;

        var plan = RenamePlanner.Build([File("a.txt"), File("b.txt")], o, NoDisk);

        Assert.Equal(["005_a.txt", "010_b.txt"], plan.Select(i => i.NewName));
    }

    [Fact]
    public void Counter_CanRestartInEveryFolder()
    {
        var o = Options(RenameMethod.Wildcard, "*.*", @"??.\2");
        o.ResetNumberPerFolder = true;

        var plan = RenamePlanner.Build(
            [File("a.jpg", @"C:\x"), File("b.jpg", @"C:\x"), File("c.jpg", @"C:\y")], o, NoDisk);

        Assert.Equal(["01.jpg", "02.jpg", "01.jpg"], plan.Select(i => i.NewName));
    }

    [Fact]
    public void Counter_ContinuesAcrossFoldersByDefault()
    {
        var o = Options(RenameMethod.Wildcard, "*.*", @"??.\2");

        var plan = RenamePlanner.Build([File("a.jpg", @"C:\x"), File("c.jpg", @"C:\y")], o, NoDisk);

        Assert.Equal(["01.jpg", "02.jpg"], plan.Select(i => i.NewName));
    }

    [Fact]
    public void Variables_NameParts()
    {
        var o = Options(RenameMethod.Wildcard, "*", @"\F_\f_\t_\e_\0");
        var entry = File("rapor.v2.docx", @"C:\Belgeler\Evrak");

        Assert.Equal("Belgeler_Evrak_rapor.v2_docx_rapor.v2.docx", Rename(entry, o));
    }

    [Fact]
    public void Variables_FolderHasNoExtension()
    {
        var o = Options(RenameMethod.Wildcard, "*", @"\t|\e");

        Assert.Equal("v1.2|", Rename(Folder("v1.2"), o));
    }

    [Fact]
    public void Variables_DateAndTimeComeFromFile()
    {
        var o = Options(RenameMethod.Wildcard, "*.*", @"\Y\m\d\H\M\S.\2");

        Assert.Equal("20260701153307.jpg", Rename(File("foto.jpg"), o));
    }

    [Fact]
    public void Variables_WeekdayAndMonthNames_FollowTheCulture()
    {
        // 1 Temmuz 2026 Çarşamba
        var o = Options(RenameMethod.Wildcard, "*", @"\A \a \B \b");
        o.Culture = new CultureInfo("tr-TR");

        Assert.Equal("Çarşamba Çar Temmuz Tem", Rename(File("x"), o));
    }

    [Fact]
    public void Variables_FileSize_WithAndWithoutThousandSeparator()
    {
        var o = Options(RenameMethod.Wildcard, "*", @"\;|\:");
        o.Culture = new CultureInfo("tr-TR");
        var big = File("x") with { Size = 1234567 };

        Assert.Equal("1.234.567|1234567", Rename(big, o));
    }

    [Fact]
    public void Variables_FullFolderPath_CannotCreateFolders()
    {
        var o = Options(RenameMethod.Wildcard, "*", @"\P");

        string name = Rename(File("x"), o);

        Assert.DoesNotContain('\\', name);
        Assert.DoesNotContain(':', name);
        Assert.Contains("Belgeler", name);
    }

    [Fact]
    public void Variables_ShortYearAndTwelveHourClock()
    {
        var o = Options(RenameMethod.Wildcard, "*", @"\y-\p\I");

        Assert.Equal("26-PM03", Rename(File("x"), o));
    }

    [Fact]
    public void Variables_CanUseCreationDate()
    {
        var entry = new FileEntry(@"C:\x", "a.txt", false, new DateTime(2020, 1, 2), new DateTime(2019, 5, 6));
        var o = Options(RenameMethod.Wildcard, "*", @"\Y\m\d");
        o.DateSource = DateSource.Created;

        Assert.Equal("20190506", Rename(entry, o));
    }

    [Fact]
    public void Backslash_EscapesLiteralCharacters()
    {
        var o = Options(RenameMethod.Wildcard, "*", @"\\\?\1");

        // "\\" is a backslash, "\?" is a literal question mark; the planner rejects both in a file name,
        // so check the raw expansion instead.
        var text = ReplacementTemplate.Expand(
            o.Replace, new ExpansionContext(File("a"), ["", "a"], 1, DateSource.Modified, CultureInfo.InvariantCulture));

        Assert.Equal(@"\?a", text);
    }

    // ---- Case changes ---------------------------------------------------------------------------------------

    [Fact]
    public void Case_UpperUntilEnd()
    {
        var o = Options(RenameMethod.Wildcard, "*.*", @"\U\1\E.\2");

        Assert.Equal("ABC.txt", Rename(File("abc.txt"), o));
    }

    [Fact]
    public void Case_LowerUntilEndAndUpperNext()
    {
        var o = Options(RenameMethod.Wildcard, "*", @"\L\u\1");

        Assert.Equal("Hello", Rename(File("hELLO", @"C:\x"), o));
    }

    [Fact]
    public void Case_UsesConfiguredCulture_ForTurkishI()
    {
        var o = Options(RenameMethod.Wildcard, "*", @"\U\1");
        o.Culture = new CultureInfo("tr-TR");

        Assert.Equal("İSTANBUL", Rename(File("istanbul"), o));
        Assert.Equal("ISTANBUL", Rename(File("istanbul"), Options(RenameMethod.Wildcard, "*", @"\U\1")));
    }

    // ---- Regular expressions --------------------------------------------------------------------------------

    [Fact]
    public void Regex_ReplacesOnlyTheMatchedPart()
    {
        var o = Options(RenameMethod.RegularExpression, @"(\..*)$", @"\L\1");

        Assert.Equal("PHOTO.jpg", Rename(File("PHOTO.JPG"), o));
    }

    [Fact]
    public void Regex_ReplacesEveryMatch()
    {
        var o = Options(RenameMethod.RegularExpression, @"\d+", "#");

        Assert.Equal("a#b#.txt", Rename(File("a1b22.txt"), o));
    }

    [Fact]
    public void Regex_CapitalizeEveryWord()
    {
        var preset = PresetFile.Load(PresetPath).Single(p => p.Name == "Her kelimenin ilk harfi büyük");

        Assert.Equal("Hello World.txt", Rename(File("hELLO wORLD.txt"), preset.ToOptions()));
    }

    [Fact]
    public void Regex_InvalidPatternIsReportedNotThrown()
    {
        var o = Options(RenameMethod.RegularExpression, "(", "x");

        var item = RenamePlanner.Build([File("a.txt")], o, NoDisk).Single();

        Assert.Equal(RenameStatus.Error, item.Status);
        Assert.NotNull(item.Message);
    }

    // ---- Translation ----------------------------------------------------------------------------------------

    [Fact]
    public void Translation_UpperAndLowerCase_SkipExtension()
    {
        Assert.Equal("ABC.txt", Rename(File("abc.txt"), Options(RenameMethod.Translation, "a-z", "A-Z")));
        Assert.Equal("abc.TXT", Rename(File("ABC.TXT"), Options(RenameMethod.Translation, "A-Z", "a-z")));
    }

    [Fact]
    public void Translation_OppositeCase()
    {
        var o = Options(RenameMethod.Translation, "a-zA-Z", "A-Za-z");

        Assert.Equal("aBc.txt", Rename(File("AbC.txt"), o));
    }

    [Fact]
    public void Translation_TurkishCharactersToAscii_LikeHocaSettings()
    {
        var o = Options(RenameMethod.Translation, "ÜüĞğİıŞşÇçÖö ", "UuGgIiSsCcOo_");

        Assert.Equal("Ogrenci_Semasi_Guncel.docx", Rename(File("Öğrenci Şeması Güncel.docx"), o));
    }

    [Fact]
    public void Translation_WorksOnFolderNames()
    {
        var o = Options(RenameMethod.Translation, "ÜüĞğİıŞşÇçÖö ", "UuGgIiSsCcOo_");

        Assert.Equal("ARSIV_Gunluk.v2", Rename(Folder("ARSIV Günlük.v2"), o));
    }

    [Fact]
    public void Translation_ExtraSearchCharactersStayUnchanged()
    {
        var o = Options(RenameMethod.Translation, "abc", "x");

        Assert.Equal("xbc", Rename(File("abc", @"C:\x"), o));
    }

    // ---- Dot files ------------------------------------------------------------------------------------------

    [Fact]
    public void DotFile_IsAllTitle()
    {
        var entry = File(".gitignore");

        Assert.Equal(".gitignore", entry.Title);
        Assert.Equal("", entry.Extension);
    }
}

public class NumberFormatTests
{
    [Theory]
    [InlineData(NumberFormat.Decimal, 7, 2, "07")]
    [InlineData(NumberFormat.Decimal, 123, 2, "123")]
    [InlineData(NumberFormat.Hexadecimal, 255, 1, "FF")]
    [InlineData(NumberFormat.Hexadecimal, 10, 3, "00A")]
    [InlineData(NumberFormat.Letters, 0, 1, "a")]
    [InlineData(NumberFormat.Letters, 25, 1, "z")]
    [InlineData(NumberFormat.Letters, 26, 1, "ba")]
    [InlineData(NumberFormat.Letters, 1, 2, "ab")]
    [InlineData(NumberFormat.Letters, 27, 3, "abb")]
    public void CountersAreWrittenInTheChosenFormat(NumberFormat format, long number, int width, string expected) =>
        Assert.Equal(expected, NumberFormatting.Format(number, width, format));

    [Fact]
    public void QuestionMarksInARule_UseTheFormat_AndCapitalsWork()
    {
        var o = Options(RenameMethod.Wildcard, "*.*", @"Resim_\U??\E.\2");
        o.NumberFormat = NumberFormat.Letters;
        o.NumberStart = 0;
        var entries = new[] { File("a.jpg"), File("b.jpg"), File("c.jpg") };

        var names = RenamePlanner.Build(entries, o, NoDisk).Select(i => i.NewName);

        Assert.Equal(["Resim_AA.jpg", "Resim_AB.jpg", "Resim_AC.jpg"], names);
    }

    [Fact]
    public void NumberedObjects_AlsoFollowTheFormat()
    {
        using var tmp = new TempFolder();
        var options = new NumberedObjectOptions
        {
            Kind = ObjectKind.EmptyFolder, Folder = tmp.Root, NamePattern = "Klasör ?", Amount = 3, Start = 9,
            NumberFormat = NumberFormat.Hexadecimal,
        };

        var plan = NumberedObjectPlanner.Build(options);

        Assert.Equal(["Klasör 9", "Klasör A", "Klasör B"], plan.Items.Select(i => i.Name));
    }
}

public class MoreTemplateVariableTests
{
    [Fact]
    public void LiteralText_BetweenQAndE_IsNotExpanded()
    {
        var o = Options(RenameMethod.Wildcard, "*", @"\Q\1-\t\E_\t");

        Assert.Equal(@"\1-\t_a", Rename(File("a"), o));
    }

    [Fact]
    public void LiteralText_WithoutE_RunsToTheEnd()
    {
        Assert.Equal(@"x\0", Rename(File("a"), Options(RenameMethod.Wildcard, "*", @"x\Q\0")));
    }

    [Fact]
    public void ZeroSuppression_RemovesTheLeadingZero()
    {
        // 1 Temmuz 2026 15:33:07
        var o = Options(RenameMethod.Wildcard, "*", @"\#d.\#m.\Y \#H:\M");

        Assert.Equal("1.7.2026 15:33", Rename(File("x"), o));
    }

    [Fact]
    public void ZeroSuppression_KeepsASingleZero()
    {
        var entry = File("x", modified: new DateTime(2026, 1, 1, 0, 0, 0));

        Assert.Equal("0", Rename(entry, Options(RenameMethod.Wildcard, "*", @"\#H")));
    }

    [Fact]
    public void DayOfYear_Weekday_AndLocaleDates()
    {
        var o = Options(RenameMethod.Wildcard, "*", @"\j|\w|\x|\X");
        o.Culture = new CultureInfo("tr-TR");

        // 1 Temmuz 2026 is the 182nd day of the year and a Wednesday (3).
        Assert.Equal("182|3|1.07.2026|15.33.07", Rename(File("x"), o));
    }

    [Fact]
    public void RandomDigits_AreRepeatable_AndDifferBetweenFiles()
    {
        var o = Options(RenameMethod.Wildcard, "*.*", @"\1_***.\2");
        var entries = Enumerable.Range(0, 30).Select(i => File($"f{i}.txt")).ToArray();

        var first = RenamePlanner.Build(entries, o, NoDisk).Select(i => i.NewName).ToList();
        var second = RenamePlanner.Build(entries, o, NoDisk).Select(i => i.NewName).ToList();

        Assert.Equal(first, second);
        Assert.All(first, n => Assert.Matches(@"^f\d+_\d{3}\.txt$", n));
        Assert.True(first.Distinct().Count() > 25);
    }
}
