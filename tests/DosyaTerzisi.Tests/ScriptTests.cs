using DosyaTerzisi.Bridge.Contract;
using DosyaTerzisi.Core;
using BridgeMethod = DosyaTerzisi.Bridge.Contract.RenameMethod;
using RenameMethod = DosyaTerzisi.Core.RenameMethod;
using static DosyaTerzisi.Tests.MediaFixtures;
using static DosyaTerzisi.Tests.TestHelpers;

namespace DosyaTerzisi.Tests;

/// <summary>Betik modu: gerçek cscript.exe ile (VBScript ve JScript).</summary>
public class ScriptTests
{
    private static IReadOnlyList<RenameItem> Plan(IReadOnlyList<FileEntry> entries, string replace, ScriptEngine engine,
        RenameMethod method = RenameMethod.Wildcard, string search = "*.*") =>
        RenamePlanner.Build(
            entries,
            new RenameOptions { Method = method, Search = search, Replace = replace, UseScript = true, Script = engine },
            NoDisk);

    private static ScriptEngine Engine(TempFolder folder, string? vbs = null, string? js = null)
    {
        if (vbs is not null) folder.File("Script.vbs", vbs);
        if (js is not null) folder.File("Script.js", js);
        return new ScriptEngine(folder.Root);
    }

    // ---- Değerlendirme -------------------------------------------------------------------------------------------

    [Fact]
    public void VbScriptExpression_UsesVariablesAsText()
    {
        using var folder = new TempFolder();
        var items = Plan([File("rapor.txt")], @"UCase(""\1"") & ""."" & ""\2""", Engine(folder));

        Assert.Equal("RAPOR.txt", items[0].NewName);
        Assert.Equal(RenameStatus.Ready, items[0].Status);
    }

    [Fact]
    public void UserFunction_FromScriptFile_IsAvailable()
    {
        using var folder = new TempFolder();
        var engine = Engine(folder, vbs: "Function Cift(s)\r\n  Cift = s & s\r\nEnd Function\r\n");
        var items = Plan([File("ab.txt")], @"Cift(""\1"") & ""."" & ""\e""", engine);

        Assert.Equal("abab.txt", items[0].NewName);
    }

    [Fact]
    public void ScriptFile_CanBeUtf16WithBom_LikeTheOldPreset()
    {
        using var folder = new TempFolder();
        System.IO.File.WriteAllText(
            Path.Combine(folder.Root, "Script.vbs"),
            "Function Sari(s)\r\n  Sari = \"ş\" & s\r\nEnd Function\r\n",
            new System.Text.UnicodeEncoding(false, true));
        var items = Plan([File("a.txt")], @"Sari(""\1"") & "".txt""", new ScriptEngine(folder.Root));

        Assert.Equal("şa.txt", items[0].NewName);
    }

    [Fact]
    public void JScript_IsUsedWhenOnlyScriptJsExists()
    {
        using var folder = new TempFolder();
        var engine = Engine(folder, js: "function Ters(s) { return s.split('').reverse().join(''); }");

        Assert.Equal("jscript", engine.Language);
        var items = Plan([File("abc.txt")], @"Ters(""\1"") + ""."" + ""\2""", engine);
        Assert.Equal("cba.txt", items[0].NewName);
    }

    [Fact]
    public void Numbers_AreAddedNotJoined_WhenNotQuoted()
    {
        using var folder = new TempFolder();
        var engine = Engine(folder);

        Assert.Equal("246", Plan([File("123.txt")], @"\1+\1", engine)[0].NewName);
        Assert.Equal("123123", Plan([File("123.txt")], @"""\1""+""\1""", engine)[0].NewName);
    }

    [Fact]
    public void Counter_AndDateVariables_AreSubstituted()
    {
        using var folder = new TempFolder();
        var items = Plan([File("a.txt"), File("b.txt")], @"""\Y-"" & ""??"" & ""-"" & ?", Engine(folder), search: "*.txt");

        // "*.txt" has one wildcard group; the counter runs 1, 2.
        Assert.Equal("2026-01-1", items[0].NewName);
        Assert.Equal("2026-02-2", items[1].NewName);
    }

    [Fact]
    public void RegularExpression_EvaluatesEveryMatch()
    {
        using var folder = new TempFolder();
        var items = Plan([File("a1b22.txt")], @"\1*2", Engine(folder), RenameMethod.RegularExpression, @"([0-9]+)");

        Assert.Equal("a2b44.txt", items[0].NewName);
    }

    // ---- Hata ve zaman aşımı -------------------------------------------------------------------------------------

    [Fact]
    public void UndefinedFunction_MarksOnlyThatItem_WithTurkishMessage()
    {
        using var folder = new TempFolder();
        var engine = Engine(folder, vbs: "Function Kontrol(s)\r\n  If s = \"kotu\" Then Err.Raise 5, \"t\", \"Olmaz\"\r\n  Kontrol = s & \"_tamam\"\r\nEnd Function\r\n");
        var items = Plan([File("iyi.txt"), File("kotu.txt"), File("sonra.txt")], @"Kontrol(""\1"") & "".txt""", engine);

        Assert.Equal(RenameStatus.Ready, items[0].Status);
        Assert.Equal("iyi_tamam.txt", items[0].NewName);
        Assert.Equal(RenameStatus.Error, items[1].Status);
        Assert.StartsWith("Betik hatası", items[1].Message);
        Assert.Contains("Olmaz", items[1].Message);
        Assert.Equal("kotu.txt", items[1].NewName);
        Assert.Equal(RenameStatus.Ready, items[2].Status);

        var unknown = Plan([File("x.txt")], @"Tanimsiz(""\1"")", engine);
        Assert.Equal(RenameStatus.Error, unknown[0].Status);
        Assert.StartsWith("Betik hatası", unknown[0].Message);
    }

    [Fact]
    public void BrokenScriptFile_FailsEveryItemWithAMessage()
    {
        using var folder = new TempFolder();
        var items = Plan([File("a.txt"), File("b.txt")], @"""\1""", Engine(folder, vbs: "Function Bozuk(\r\n"));

        Assert.All(items, i =>
        {
            Assert.Equal(RenameStatus.Error, i.Status);
            Assert.Contains("betik dosyası", i.Message);
        });
    }

    [Fact]
    public void InfiniteLoop_IsKilledAfterTheTimeout()
    {
        using var folder = new TempFolder();
        var engine = Engine(folder, vbs: "Function Sonsuz(s)\r\n  Do\r\n  Loop\r\nEnd Function\r\n");
        engine.Timeout = TimeSpan.FromSeconds(1.5);

        var started = DateTime.UtcNow;
        var items = Plan([File("a.txt"), File("b.txt")], @"Sonsuz(""\1"")", engine);

        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(15));
        Assert.All(items, i =>
        {
            Assert.Equal(RenameStatus.Error, i.Status);
            Assert.Equal("Betik zaman aşımına uğradı", i.Message);
        });
    }

    // ---- Güvenlik: özel karakterler ------------------------------------------------------------------------------

    public static TheoryData<string> Names { get; } = new()
    {
        "plain.txt",
        "tırnak\" & Chr(65) & \".txt",
        "tek'tırnak.txt",
        "a&b & c.txt",
        "ters\\bölü\\n\\\\.txt",
        "yeni\nsatır\rve\ttab.txt",
        "\")\r\n: MsgBox(1) : (\".txt",
    };

    [Theory]
    [MemberData(nameof(Names))]
    public void SpecialCharacters_TravelAsPlainText(string name)
    {
        using var folder = new TempFolder();
        foreach (bool js in new[] { false, true })
        {
            var engine = js ? Engine(folder, js: "function Aynen(s) { return s; }") : Engine(folder, vbs: "Function Aynen(s)\r\n  Aynen = s\r\nEnd Function\r\n");
            var ctx = new ExpansionContext(File(name), ["", name], 1, DateSource.Modified, System.Globalization.CultureInfo.InvariantCulture);

            var quoted = engine.Evaluate([ScriptTemplate.Build(@"Aynen(""\0"")", engine.Language, ctx)]);
            Assert.Null(quoted[0].Error);
            Assert.Equal(name, quoted[0].Value);

            // Tırnak dışında sayı olmayan değer, ifadeyi bozamaz: metin olarak girer.
            var bare = engine.Evaluate([ScriptTemplate.Build(@"Aynen(\0)", engine.Language, ctx)]);
            Assert.Null(bare[0].Error);
            Assert.Equal(name, bare[0].Value);

            System.IO.File.Delete(Path.Combine(folder.Root, js ? "Script.js" : "Script.vbs"));
        }
    }

    [Fact]
    public void FolderVariable_IsRawPath_WithTrailingBackslash()
    {
        using var folder = new TempFolder();
        foreach (bool js in new[] { false, true })
        {
            var engine = js ? Engine(folder, js: "var x = 1;") : Engine(folder);
            var ctx = new ExpansionContext(File("x.txt", @"C:\Belgeler\Evrak"), [], 1, DateSource.Modified, System.Globalization.CultureInfo.InvariantCulture);
            var result = engine.Evaluate([ScriptTemplate.Build(@"""\P\0""", engine.Language, ctx)]);

            Assert.Equal(@"C:\Belgeler\Evrak\x.txt", result[0].Value);
            if (js) System.IO.File.Delete(Path.Combine(folder.Root, "Script.js"));
        }
    }

    // ---- Tek süreç -----------------------------------------------------------------------------------------------

    [Fact]
    public void FiftyFiles_AreEvaluatedInOneProcess()
    {
        using var folder = new TempFolder();
        var engine = Engine(folder);
        var entries = Enumerable.Range(1, 50).Select(n => File($"dosya{n}.txt")).ToList();

        var items = Plan(entries, @"UCase(""\1"") & ""."" & ""\2""", engine);

        Assert.Equal(1, engine.LaunchCount);
        Assert.Equal(50, items.Count);
        Assert.All(items.Zip(entries), p => Assert.Equal(p.Second.Name.ToUpperInvariant().Replace(".TXT", ".txt"), p.First.NewName));
    }

    [Fact]
    public void WithoutScript_NoProcessIsStarted()
    {
        using var folder = new TempFolder();
        var engine = Engine(folder);
        RenamePlanner.Build([File("a.txt")], new RenameOptions { Search = "*", Replace = @"x\1", Script = engine }, NoDisk);

        Assert.Equal(0, engine.LaunchCount);
    }

    [Fact]
    public void TemporaryFiles_AreRemoved()
    {
        using var folder = new TempFolder();
        var engine = Engine(folder);
        Plan([File("a.txt")], @"""\1""", engine);

        Assert.NotNull(engine.LastWorkFolder);
        Assert.False(Directory.Exists(engine.LastWorkFolder));
    }

    // ---- Etiket adlandırma ---------------------------------------------------------------------------------------

    private static readonly FakeTags Song = new(
        Text("ID3.Artist", "Queen"),
        Text("ID3.Track", "Bohemian Rhapsody"),
        Number("ID3.TrackNumber", 3));

    [Fact]
    public void TagRename_EvaluatesTheTemplateAsScript()
    {
        using var folder = new TempFolder();
        var options = new TagRenameOptions
        {
            Template = @"UCase(""<ID3.Artist>"") & "" - "" & ""<ID3.Track>.mp3"" & ""-"" & (<ID3.TrackNumber> + 1)",
            UseScript = true,
            Script = Engine(folder),
        };
        var items = TagRenamePlanner.Build([File("x.mp3")], options, Song, NoDisk);

        Assert.Equal("QUEEN - Bohemian Rhapsody.mp3-4", items[0].NewName);
    }

    [Fact]
    public void TagRename_MissingTagStillSkips_AndScriptErrorsAreReported()
    {
        using var folder = new TempFolder();
        var engine = Engine(folder);
        var missing = TagRenamePlanner.Build(
            [File("x.mp3")],
            new TagRenameOptions { Template = @"""<ID3.Album>""", UseScript = true, Script = engine },
            Song, NoDisk);
        Assert.Equal(RenameStatus.MissingTag, missing[0].Status);

        var broken = TagRenamePlanner.Build(
            [File("x.mp3")],
            new TagRenameOptions { Template = @"Yok(""<ID3.Artist>"")", UseScript = true, Script = engine },
            Song, NoDisk);
        Assert.Equal(RenameStatus.Error, broken[0].Status);
        Assert.StartsWith("Betik hatası", broken[0].Message);
    }

    // ---- Köprü ---------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Bridge_ScriptInfo_EditScript_AndPreview()
    {
        using var scripts = new TempFolder();
        using var h = new BridgeHarness(scriptFolder: scripts.Root);
        h.Files.File("rapor.txt");
        await h.Scan();

        var info = await h.Call<ScriptInfo>("scriptInfo", new Empty());
        Assert.False(info.Exists);
        Assert.Equal("vbscript", info.Language);
        Assert.Equal(Path.Combine(scripts.Root, "Script.vbs"), info.Path);

        var edited = await h.Call<ScriptInfo>("editScript", new Empty());
        Assert.True(edited.Exists);
        Assert.Equal([info.Path], h.Dialogs.OpenedInEditor);
        Assert.Contains("Function Buyuk", System.IO.File.ReadAllText(info.Path));

        // Örnek betikteki işlev kullanılabilir.
        var rule = BridgeHarness.Rule(BridgeMethod.Wildcard, "*.*", @"Buyuk(""\1"") & ""."" & ""\2""") with { UseScript = true };
        var preview = await h.Preview(rule);
        Assert.Equal("RAPOR.txt", preview.Rows[0].NewName);

        // İkinci kez açmak dosyanın üstüne yazmaz.
        System.IO.File.AppendAllText(info.Path, "\r\n' benim notum\r\n");
        await h.Call<ScriptInfo>("editScript", new Empty());
        Assert.Contains("benim notum", System.IO.File.ReadAllText(info.Path));
        Assert.Equal(2, h.Dialogs.OpenedInEditor.Count);
    }

    [Fact]
    public async Task Bridge_ScriptError_ShowsInThePreviewRow()
    {
        using var scripts = new TempFolder();
        using var h = new BridgeHarness(scriptFolder: scripts.Root);
        h.Files.File("a.txt");
        h.Files.File("b.txt");
        await h.Scan();

        var rule = BridgeHarness.Rule(BridgeMethod.Wildcard, "*.*", @"Yok(""\1"")") with { UseScript = true };
        var preview = await h.Preview(rule);

        Assert.All(preview.Rows, r => Assert.StartsWith("Betik hatası", r.Message));
        Assert.Equal(0, preview.Ready);
    }
}
