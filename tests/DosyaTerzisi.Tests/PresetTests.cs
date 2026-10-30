using DosyaTerzisi.Core;
using static DosyaTerzisi.Tests.TestHelpers;

namespace DosyaTerzisi.Tests;

/// <summary>Runs the shipped presets (<c>OnAyarlar.txt</c>) through the engine.</summary>
public class PresetTests
{
    private static RenamePreset Preset(string name) => PresetFile.Load(PresetPath).Single(p => p.Name == name);

    private static string Run(string presetName, FileEntry entry, int start = 1)
    {
        var options = Preset(presetName).ToOptions();
        options.NumberStart = start;
        return Rename(entry, options);
    }

    [Fact]
    public void PresetFile_LoadsAllShippedPresets()
    {
        var presets = PresetFile.Load(PresetPath);

        Assert.True(presets.Count >= 15);
        Assert.Equal("Tarih ve saat", presets[0].Name);
        Assert.Equal(RenameMethod.Wildcard, presets[0].Method);
        Assert.Equal(@"\Y\m\d\H\M\S.\2", presets[0].Replace);
        Assert.Contains(presets, p => p.Method == RenameMethod.RegularExpression);
        Assert.Contains(presets, p => p.Method == RenameMethod.Translation);
    }

    [Fact]
    public void PresetFile_IgnoresCommentsAndJunk()
    {
        var presets = PresetFile.Parse("[Preset]\r\n;Preset=0\ta\r\nPreset=9\t\ta\tb\tbad mode\r\nPreset=0\t\t*\tx\tgood\r\n");

        Assert.Equal("good", presets.Single().Name);
    }

    [Fact]
    public void Every_Preset_RunsWithoutError()
    {
        foreach (var preset in PresetFile.Load(PresetPath))
        {
            var item = RenamePlanner.Build([File("Sample (1) - Copy.JPG")], preset.ToOptions(), NoDisk).Single();

            Assert.True(item.Status != RenameStatus.Error, $"{preset.Name}: {item.Message}");
        }
    }

    [Fact]
    public void DateTime() => Assert.Equal("20260701153307.jpg", Run("Tarih ve saat", File("foto.jpg")));

    [Fact]
    public void NumberPlusExtension() => Assert.Equal("07.jpg", Run("Sıra no + uzantı", File("foto.jpg"), start: 7));

    [Fact]
    public void NumberPlusFileTitlePlusExtension() =>
        Assert.Equal("01_foto.jpg", Run("Sıra no + ad + uzantı", File("foto.jpg")));

    [Fact]
    public void FileTitlePlusNumberPlusExtension() =>
        Assert.Equal("foto_01.jpg", Run("Ad + sıra no + uzantı", File("foto.jpg")));

    [Fact]
    public void SwapBothSideOfDash() => Assert.Equal("b-a.txt", Run("“-” işaretinin iki yanını değiştir", File("a-b.txt")));

    [Fact]
    public void ReplaceDscWithImg() => Assert.Equal("IMG_0042.jpg", Run("“DSC” yerine “IMG” yaz", File("DSC_0042.jpg")));

    [Fact]
    public void RemoveParenthesisNumber() => Assert.Equal("report.txt", Run("(n) sil", File("report(2).txt")));

    [Fact]
    public void RemoveBracketNumber() => Assert.Equal("report.txt", Run("[n] sil", File("report[2].txt")));

    [Fact]
    public void RemoveKopya() =>
        Assert.Equal("rapor.docx", Run("“ - Kopya” ibaresini sil", File("rapor - Kopya (2).docx")));

    [Fact]
    public void LowerCaseExtension() => Assert.Equal("PHOTO.jpg", Run("Uzantıyı küçük harf yap", File("PHOTO.JPG")));

    [Fact]
    public void Capitalize() => Assert.Equal("Hello World.txt", Run("Her kelimenin ilk harfi büyük", File("hello world.txt")));

    [Fact]
    public void UpperCase() => Assert.Equal("ABC.txt", Run("Büyük harf", File("abc.txt")));

    [Fact]
    public void LowerCase() => Assert.Equal("abc.TXT", Run("Küçük harf", File("ABC.TXT")));

    [Fact]
    public void OppositeCase() => Assert.Equal("aBc.txt", Run("Büyük/küçük harfi tersine çevir", File("AbC.txt")));

    [Fact]
    public void TurkishLettersAreSimplified() =>
        Assert.Equal("Ogrenci Sasmaz.txt", Run("Türkçe harfleri sadeleştir", File("Öğrenci Şaşmaz.txt")));

    [Fact]
    public void SpacesBecomeUnderscores() => Assert.Equal("a_b_c.txt", Run("Boşlukları alt çizgi yap", File("a b c.txt")));
}
