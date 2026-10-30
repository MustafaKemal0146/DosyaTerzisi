using System.Text.Json;
using DosyaTerzisi.Bridge;
using DosyaTerzisi.Bridge.Contract;
using Core = DosyaTerzisi.Core;

namespace DosyaTerzisi.Tests;

public sealed class FakeDialogs : INativeDialogs
{
    public string? Folder { get; set; }
    public string? OpenFile { get; set; }
    public string? SaveFile { get; set; }
    public string? AskedFolder { get; private set; }
    public List<string> OpenedInEditor { get; } = [];

    public Task OpenInEditorAsync(string path)
    {
        OpenedInEditor.Add(path);
        return Task.CompletedTask;
    }

    public Task<string?> PickFolderAsync(string? initialFolder)
    {
        AskedFolder = initialFolder;
        return Task.FromResult(Folder);
    }

    public Task<string?> PickOpenFileAsync(string title, string filter) => Task.FromResult(OpenFile);

    public Task<string?> PickSaveFileAsync(string title, string filter, string defaultName) => Task.FromResult(SaveFile);
}

public sealed class BridgeError(string code, string message) : Exception($"{code}: {message}")
{
    public string Code { get; } = code;
}

/// <summary>Gerçek bir oturum ve dosyalar; istekler ve cevaplar gerçek JSON olarak gider.</summary>
public sealed class BridgeHarness : IDisposable
{
    private readonly string _stateDir = Path.Combine(Path.GetTempPath(), "dosyaterzisi-bridge-" + Guid.NewGuid().ToString("N"));
    private readonly TempFolder _files = new();
    private int _nextId;

    public FakeDialogs Dialogs { get; } = new();
    public BridgeSession Session { get; private set; } = null!;
    public BridgeDispatcher Dispatcher { get; private set; } = null!;
    public string Root => _files.Root;
    public TempFolder Files => _files;

    public BridgeHarness(string? presetFile = null, string initialFolder = "", IReadOnlyList<string>? initialPaths = null, IShellIntegration? integration = null, string? scriptFolder = null)
    {
        Restart(presetFile, initialFolder, initialPaths, integration, scriptFolder);
    }

    /// <summary>Programı kapatıp açmak gibi: durum dosyaları kalır, bellekteki her şey sıfırlanır.</summary>
    public void Restart(string? presetFile = null, string initialFolder = "", IReadOnlyList<string>? initialPaths = null, IShellIntegration? integration = null, string? scriptFolder = null)
    {
        Session = new BridgeSession(
            new Core.RenameService(new Core.HistoryStore(Path.Combine(_stateDir, "history.json"))),
            new Core.FavoriteStore(Path.Combine(_stateDir, "favorites.json")),
            Dialogs,
            presetFile,
            initialFolder,
            initialPaths,
            integration,
            scriptFolder);
        Dispatcher = new BridgeDispatcher(Session);
    }

    public string HistoryPath => Path.Combine(_stateDir, "history.json");
    public string FavoritesPath => Path.Combine(_stateDir, "favorites.json");

    public static readonly ListingOptions AllFiles = new(true, false, "*", false, 10);

    public static Rule Rule(RenameMethod method, string search, string replace, bool autoNumber = false) =>
        new(method, search, replace, false, 1, 1, false, autoNumber, false, Operation.Rename, "", NumberStyle.Decimal, false, "", false, false);

    /// <summary>İsteği JSON olarak gönderir, cevabı sözleşmeye göre denetler ve çevirir.</summary>
    public async Task<T> Call<T>(string method, object parameters)
    {
        string request = JsonSerializer.Serialize(
            new { v = Wire.Version, id = ++_nextId, method, @params = parameters }, Wire.Options);
        using var response = JsonDocument.Parse(await Dispatcher.HandleAsync(request));
        var root = response.RootElement;

        Assert.Equal(Wire.Version, root.GetProperty("v").GetInt32());
        Assert.Equal(_nextId, root.GetProperty("id").GetInt32());

        if (root.TryGetProperty("error", out var error))
            throw new BridgeError(error.GetProperty("code").GetString()!, error.GetProperty("message").GetString()!);

        var result = root.GetProperty("result");
        Assert.Empty(ContractValidator.Default.ValidateResult(method, result));
        return JsonSerializer.Deserialize<T>(result.GetRawText(), Wire.Options)!;
    }

    public Task<InitState> Init() => Call<InitState>("init", new Empty());

    public Task<ScanResult> Scan(ListingOptions? options = null) =>
        Call<ScanResult>("scan", new ScanParams(Root, options ?? AllFiles));

    public Task<Preview> Preview(Rule rule, params int[] selected) =>
        Call<Preview>("preview", new PreviewParams(rule, [.. selected], []));

    public Task<Preview> PreviewWith(Rule rule, List<NameOverride> typed, params int[] selected) =>
        Call<Preview>("preview", new PreviewParams(rule, [.. selected], typed));

    public async Task<RenameOutcome> Rename(RenameDecision decision = RenameDecision.Proceed, string description = "deneme")
    {
        await Call<RenameRequest>("prepareRename", new Empty());
        return await Call<RenameOutcome>("executeRename", new ExecuteRenameParams(decision, description));
    }

    public void Dispose()
    {
        _files.Dispose();
        if (Directory.Exists(_stateDir)) Directory.Delete(_stateDir, recursive: true);
    }
}

public class BridgeTests
{
    private static Rule Wildcard(string search, string replace, bool auto = false) =>
        BridgeHarness.Rule(RenameMethod.Wildcard, search, replace, auto);

    // ---- Başlangıç -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task Init_GivesPresets_Favorites_AndTheStartFolder()
    {
        using var h = new BridgeHarness(TestHelpers.PresetPath, initialFolder: @"C:\Belgeler");
        await h.Call<List<Favorite>>("addFavorite", new AddFavoriteParams("Benimki", new SavedRule(RenameMethod.Translation, "a-z", "A-Z", false)));

        var init = await h.Init();

        Assert.Equal(@"C:\Belgeler", init.Folder);
        Assert.Contains(init.Presets, p => p.Title == "Tarih ve saat" && p.Rule.Method == RenameMethod.Wildcard);
        Assert.Contains(init.Presets, p => p.Title == "(n) sil" && p.Rule.Method == RenameMethod.Regex);
        Assert.Equal(["Benimki"], init.Favorites.Select(f => f.Name));
        Assert.Empty(init.History);
        Assert.Empty(init.Notices);
    }

    [Fact]
    public async Task Init_WithoutAPresetFile_HasNoPresets()
    {
        using var h = new BridgeHarness(presetFile: Path.Combine(Path.GetTempPath(), "yok.ini"));

        Assert.Empty((await h.Init()).Presets);
    }

    [Fact]
    public async Task Init_ExplainsAnInterruptedRename_OnlyOnce()
    {
        using var h = new BridgeHarness();
        h.Files.File("a.txt");
        h.Files.File("b.txt");
        new Core.HistoryStore(h.HistoryPath).Begin("yarım kalan",
        [
            new Core.RenameMove(h.Root, "a.txt", "x_a.txt", false),
            new Core.RenameMove(h.Root, "b.txt", "x_b.txt", false),
        ]);
        File.Move(Path.Combine(h.Root, "a.txt"), Path.Combine(h.Root, "x_a.txt"));
        h.Restart();

        var first = await h.Init();
        var second = await h.Init();

        var notice = Assert.Single(first.Notices);
        Assert.Equal("Yarım kalan işlem", notice.Title);
        Assert.Contains("2 öğeden 1 tanesi", notice.Message);
        Assert.Empty(second.Notices);
        Assert.Single(first.History);

        await h.Call<UndoOutcome?>("undo", new Empty());
        Assert.Equal(["a.txt", "b.txt"], h.Files.Tree());
    }

    [Fact]
    public async Task Init_ReportsABrokenHistoryFile()
    {
        using var h = new BridgeHarness();
        Directory.CreateDirectory(Path.GetDirectoryName(h.HistoryPath)!);
        File.WriteAllText(h.HistoryPath, "bozuk");
        h.Restart();

        var init = await h.Init();

        Assert.Contains(init.Notices, n => n.Message.Contains("okunamadı"));
    }

    // ---- Liste ---------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Scan_ListsFilesInNaturalOrder_WithIdsAsPositions()
    {
        using var h = new BridgeHarness();
        h.Files.File("c10.txt", "xx");
        h.Files.File("c2.txt");
        h.Files.File("Şu.txt");

        var scan = await h.Scan();

        Assert.Equal(["c2.txt", "c10.txt", "Şu.txt"], scan.Rows.Select(r => r.Name));
        Assert.Equal([0, 1, 2], scan.Rows.Select(r => r.Id));
        Assert.Equal(2, scan.Rows[1].Size);
        Assert.All(scan.Rows, r => Assert.Equal(h.Root, r.Dir));
        Assert.All(scan.Rows, r => Assert.True(r.Modified > DateTimeOffset.UtcNow.AddMinutes(-5).ToUnixTimeMilliseconds()));
    }

    [Fact]
    public async Task Scan_Options_FilterDepthAndFolders()
    {
        using var h = new BridgeHarness();
        h.Files.File("a.txt");
        h.Files.File("b.png");
        h.Files.File(@"alt\c.txt");
        h.Files.File(@"alt\derin\d.txt");

        Assert.Equal(["a.txt"], (await h.Scan(new ListingOptions(true, false, "*.txt", false, 10))).Rows.Select(r => r.Name));
        Assert.Equal(["a.txt", "c.txt"], (await h.Scan(new ListingOptions(true, false, "*.txt", true, 1))).Rows.Select(r => r.Name));
        Assert.Equal(["a.txt", "c.txt", "d.txt"], (await h.Scan(new ListingOptions(true, false, "*.txt", true, 5))).Rows.Select(r => r.Name));
        Assert.Equal(["alt"], (await h.Scan(new ListingOptions(false, true, "*", false, 1))).Rows.Select(r => r.Name));
    }

    [Fact]
    public async Task Scan_OfAMissingFolder_IsAnError()
    {
        using var h = new BridgeHarness();

        var error = await Assert.ThrowsAsync<BridgeError>(
            () => h.Call<ScanResult>("scan", new ScanParams(Path.Combine(h.Root, "yok"), BridgeHarness.AllFiles)));

        Assert.Equal(ErrorCodes.Failed, error.Code);
        Assert.Contains("Klasör bulunamadı", error.Message);
    }

    // ---- Önizleme ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Preview_GivesARowForEveryFile_InListOrder()
    {
        using var h = new BridgeHarness();
        h.Files.File("a - b.txt");
        h.Files.File("düz.txt");
        await h.Scan();

        var preview = await h.Preview(Wildcard("* - *", @"\1-\2"));

        Assert.Equal([0, 1], preview.Rows.Select(r => r.Id));
        Assert.Equal(["a-b.txt", "düz.txt"], preview.Rows.Select(r => r.NewName));
        Assert.Equal([RowStatus.Ready, RowStatus.Unchanged], preview.Rows.Select(r => r.Status));
        Assert.Equal(1, preview.Ready);
        Assert.Equal(0, preview.Problems);
        Assert.Equal("", preview.RuleError);
    }

    [Fact]
    public async Task Preview_TurkishToAscii_LikeTheOldProgram()
    {
        using var h = new BridgeHarness();
        h.Files.File("Öğrenci Şeması.docx");
        await h.Scan();

        var rule = BridgeHarness.Rule(RenameMethod.Translation, "ÜüĞğİıŞşÇçÖö ", "UuGgIiSsCcOo_");

        Assert.Equal("Ogrenci_Semasi.docx", (await h.Preview(rule)).Rows.Single().NewName);
    }

    [Fact]
    public async Task Preview_OnlyTheSelectedRows_AreRenamed_AndNumberedAmongThemselves()
    {
        using var h = new BridgeHarness();
        foreach (var n in new[] { "a", "b", "c" }) h.Files.File(n + ".txt");
        await h.Scan();

        var preview = await h.Preview(Wildcard("*.*", @"??_\1.\2"), 1, 2);

        Assert.Equal(["a.txt", "01_b.txt", "02_c.txt"], preview.Rows.Select(r => r.NewName));
        Assert.Equal(2, preview.Ready);
    }

    [Fact]
    public async Task Preview_BrokenRegex_ShowsTheRuleError()
    {
        using var h = new BridgeHarness();
        h.Files.File("a.txt");
        await h.Scan();

        var preview = await h.Preview(BridgeHarness.Rule(RenameMethod.Regex, "(", "x"));

        Assert.NotEqual("", preview.RuleError);
        Assert.Equal(RowStatus.Error, preview.Rows.Single().Status);
        Assert.Equal(0, preview.Ready);
        Assert.Equal(1, preview.Problems);
    }

    [Fact]
    public async Task Preview_MarksConflicts_AndAutoNumberSolvesThem()
    {
        using var h = new BridgeHarness();
        h.Files.File("a.txt");
        h.Files.File("b.txt");
        await h.Scan();

        var reported = await h.Preview(Wildcard("*.*", @"ayni.\2"));
        Assert.Equal([RowStatus.Ready, RowStatus.Conflict], reported.Rows.Select(r => r.Status));
        Assert.Equal(1, reported.Problems);

        var numbered = await h.Preview(Wildcard("*.*", @"ayni.\2", auto: true));
        Assert.Equal(["ayni.txt", "ayni(01).txt"], numbered.Rows.Select(r => r.NewName));
        Assert.Equal(0, numbered.Problems);
    }

    [Fact]
    public async Task Preview_BeforeAnyScan_IsEmpty()
    {
        using var h = new BridgeHarness();

        var preview = await h.Preview(Wildcard("*", @"\1"));

        Assert.Empty(preview.Rows);
        Assert.Equal(0, preview.Ready);
    }

    // ---- Adlandırma ve geri alma ---------------------------------------------------------------------------------

    [Fact]
    public async Task PrepareRename_ListsConflicts_AndCountsOtherProblems()
    {
        using var h = new BridgeHarness();
        h.Files.File("a.txt");
        h.Files.File("b.txt");
        h.Files.File("c.txt");
        await h.Scan();
        await h.Preview(Wildcard("*.*", @"ayni.\2"));

        var request = await h.Call<RenameRequest>("prepareRename", new Empty());

        Assert.Equal(1, request.Ready);
        Assert.Equal(["b.txt", "c.txt"], request.Conflicts.Select(c => c.CurrentName));
        Assert.All(request.Conflicts, c => Assert.Equal("ayni.txt", c.WantedName));
        Assert.Equal(0, request.OtherProblems);

        await h.Preview(Wildcard("*", "CON"));
        var invalid = await h.Call<RenameRequest>("prepareRename", new Empty());
        Assert.Equal(3, invalid.OtherProblems);
    }

    [Fact]
    public async Task PrepareRename_WithoutAPreview_HasNothing()
    {
        using var h = new BridgeHarness();

        var request = await h.Call<RenameRequest>("prepareRename", new Empty());

        Assert.Equal(0, request.Ready);
        Assert.Empty(request.Conflicts);
    }

    [Fact]
    public async Task Rename_ChangesTheDisk_RecordsHistory_AndUndoRestores()
    {
        using var h = new BridgeHarness();
        h.Files.File("Öğrenci Şeması.docx", "içerik");
        await h.Scan();
        await h.Preview(BridgeHarness.Rule(RenameMethod.Translation, "ÜüĞğİıŞşÇçÖö ", "UuGgIiSsCcOo_"));

        var outcome = await h.Rename(description: "Türkçe → ASCII");

        Assert.Equal(1, outcome.Renamed);
        Assert.Empty(outcome.Failures);
        Assert.Equal(["Ogrenci_Semasi.docx"], h.Files.Tree());

        var history = await h.Call<List<HistoryItem>>("history", new Empty());
        var item = Assert.Single(history);
        Assert.Equal("Türkçe → ASCII", item.Description);
        Assert.Equal(1, item.Count);

        var undo = await h.Call<UndoOutcome?>("undo", new Empty());

        Assert.NotNull(undo);
        Assert.Equal("Türkçe → ASCII", undo.Description);
        Assert.Equal(1, undo.Restored);
        Assert.Equal(["Öğrenci Şeması.docx"], h.Files.Tree());
        Assert.Empty(await h.Call<List<HistoryItem>>("history", new Empty()));
        Assert.Null(await h.Call<UndoOutcome?>("undo", new Empty()));
    }

    [Fact]
    public async Task Rename_OfTheSelectionOnly()
    {
        using var h = new BridgeHarness();
        foreach (var n in new[] { "a", "b", "c" }) h.Files.File(n + ".txt");
        await h.Scan();
        await h.Preview(Wildcard("*", "x_" + @"\1"), 0);

        await h.Rename();

        Assert.Equal(["b.txt", "c.txt", "x_a.txt"], h.Files.Tree());
    }

    [Fact]
    public async Task Rename_Decisions_SkipConflicts_Number_OrCancel()
    {
        foreach (var (decision, expected) in new[]
        {
            (RenameDecision.Proceed, new[] { "b.txt", "c.txt", "ayni.txt" }),
            (RenameDecision.AutoNumber, new[] { "ayni(01).txt", "ayni(02).txt", "ayni.txt" }),
            (RenameDecision.Cancel, new[] { "a.txt", "b.txt", "c.txt" }),
        })
        {
            using var h = new BridgeHarness();
            foreach (var n in new[] { "a", "b", "c" }) h.Files.File(n + ".txt");
            await h.Scan();
            await h.Preview(Wildcard("*.*", @"ayni.\2"));

            await h.Rename(decision);

            Assert.Equal(expected.Order(StringComparer.Ordinal), h.Files.Tree());
        }
    }

    [Fact]
    public async Task Rename_ReportsFilesItCouldNotChange_AndStillChangesTheRest()
    {
        using var h = new BridgeHarness();
        var locked = h.Files.File("a.txt");
        h.Files.File("b.txt");
        await h.Scan();
        await h.Preview(Wildcard("*", "x_" + @"\1"));

        RenameOutcome outcome;
        using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
            outcome = await h.Rename();

        Assert.Equal(1, outcome.Renamed);
        var failure = Assert.Single(outcome.Failures);
        Assert.EndsWith("a.txt", failure.Path);
        Assert.Contains("x_a.txt", failure.Message);
        Assert.Equal(["a.txt", "x_b.txt"], h.Files.Tree());
    }

    [Fact]
    public async Task Undo_StepsBack_AndHistoryIsNewestFirst_AndSurvivesARestart()
    {
        using var h = new BridgeHarness();
        h.Files.File("a.txt");
        await h.Scan();
        await h.Preview(Wildcard("*", @"1_\1"));
        await h.Rename(description: "bir");
        await h.Scan();
        await h.Preview(Wildcard("*", @"2_\1"));
        await h.Rename(description: "iki");
        Assert.Equal(["2_1_a.txt"], h.Files.Tree());

        h.Restart();
        var history = (await h.Init()).History;
        Assert.Equal(["iki", "bir"], history.Select(x => x.Description));

        Assert.Equal("iki", (await h.Call<UndoOutcome?>("undo", new Empty()))!.Description);
        Assert.Equal(["1_a.txt"], h.Files.Tree());
        Assert.Equal("bir", (await h.Call<UndoOutcome?>("undo", new Empty()))!.Description);
        Assert.Equal(["a.txt"], h.Files.Tree());
    }

    // ---- Klasör seçici -------------------------------------------------------------------------------------------

    [Fact]
    public async Task PickFolder_UsesTheNativeDialog()
    {
        using var h = new BridgeHarness();
        h.Dialogs.Folder = @"C:\Seçilen";

        var picked = await h.Call<string?>("pickFolder", new PickFolderParams(h.Root));

        Assert.Equal(@"C:\Seçilen", picked);
        Assert.Equal(h.Root, h.Dialogs.AskedFolder);

        h.Dialogs.Folder = null;
        Assert.Null(await h.Call<string?>("pickFolder", new PickFolderParams(@"C:\olmayan\yol")));
        Assert.Null(h.Dialogs.AskedFolder);
    }

    // ---- Favoriler -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task Favorites_AddUpdateMoveRemove()
    {
        using var h = new BridgeHarness();
        var rule = new SavedRule(RenameMethod.Regex, @"\d+", "#", true);

        await h.Call<List<Favorite>>("addFavorite", new AddFavoriteParams("Bir", rule));
        var list = await h.Call<List<Favorite>>("addFavorite", new AddFavoriteParams("İki", rule with { Search = "x" }));
        Assert.Equal(["Bir", "İki"], list.Select(f => f.Name));
        Assert.Equal(rule, list[0].Rule);

        list = await h.Call<List<Favorite>>("moveFavorite", new MoveFavoriteParams(list[1].Id, -1));
        Assert.Equal(["İki", "Bir"], list.Select(f => f.Name));

        list = await h.Call<List<Favorite>>("updateFavorite", new UpdateFavoriteParams(list[0] with { Name = "Yeni ad" }));
        Assert.Equal(["Yeni ad", "Bir"], list.Select(f => f.Name));

        list = await h.Call<List<Favorite>>("removeFavorite", new RemoveFavoriteParams(list[0].Id));
        Assert.Equal(["Bir"], list.Select(f => f.Name));

        h.Restart();
        Assert.Equal(["Bir"], (await h.Init()).Favorites.Select(f => f.Name));
    }

    [Fact]
    public async Task Favorites_BadIds_AreRejected()
    {
        using var h = new BridgeHarness();

        var error = await Assert.ThrowsAsync<BridgeError>(
            () => h.Call<List<Favorite>>("removeFavorite", new RemoveFavoriteParams("bu-bir-kimlik-degil")));
        Assert.Equal(ErrorCodes.BadRequest, error.Code);

        var missing = await Assert.ThrowsAsync<BridgeError>(() => h.Call<List<Favorite>>(
            "updateFavorite",
            new UpdateFavoriteParams(new Favorite(Guid.NewGuid().ToString("N"), "x", new SavedRule(RenameMethod.Wildcard, "*", "x", false)))));
        Assert.Equal(ErrorCodes.Failed, missing.Code);
    }

    [Fact]
    public async Task Favorites_ExportThenImport()
    {
        using var h = new BridgeHarness();
        string file = Path.Combine(h.Root, "..", "dosyaterzisi-fav-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            await h.Call<List<Favorite>>("addFavorite", new AddFavoriteParams("A", new SavedRule(RenameMethod.Translation, "a-z", "A-Z", false)));

            Assert.Equal(new ExportResult(false, 0), await h.Call<ExportResult>("exportFavorites", new Empty()));

            h.Dialogs.SaveFile = file;
            Assert.Equal(new ExportResult(true, 1), await h.Call<ExportResult>("exportFavorites", new Empty()));

            using var other = new BridgeHarness();
            other.Dialogs.OpenFile = file;
            var imported = await other.Call<ImportResult>("importFavorites", new Empty());
            Assert.Equal(1, imported.Added);
            Assert.Equal("A", imported.Favorites.Single().Name);

            other.Dialogs.OpenFile = null;
            Assert.Equal(0, (await other.Call<ImportResult>("importFavorites", new Empty())).Added);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public async Task Favorites_ImportOfABrokenFile_IsAnError()
    {
        using var h = new BridgeHarness();
        string file = Path.Combine(h.Root, "..", "dosyaterzisi-bad-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(file, "bozuk");
        try
        {
            h.Dialogs.OpenFile = file;

            var error = await Assert.ThrowsAsync<BridgeError>(() => h.Call<ImportResult>("importFavorites", new Empty()));

            Assert.Equal(ErrorCodes.Failed, error.Code);
        }
        finally
        {
            File.Delete(file);
        }
    }

    // ---- Zarf ve kötü istekler -----------------------------------------------------------------------------------

    private static async Task<(string Code, string Message, JsonElement Id)> Fail(BridgeHarness h, string request)
    {
        using var document = JsonDocument.Parse(await h.Dispatcher.HandleAsync(request));
        var root = document.RootElement.Clone();
        Assert.False(root.TryGetProperty("result", out _));
        var error = root.GetProperty("error");
        return (error.GetProperty("code").GetString()!, error.GetProperty("message").GetString()!, root.GetProperty("id"));
    }

    [Fact]
    public async Task BadRequests_GetErrorResponses_NeverExceptions()
    {
        using var h = new BridgeHarness();

        var notJson = await Fail(h, "bu json değil");
        Assert.Equal(ErrorCodes.BadRequest, notJson.Code);
        Assert.Equal(JsonValueKind.Null, notJson.Id.ValueKind);

        Assert.Equal(ErrorCodes.BadRequest, (await Fail(h, "[1,2]")).Code);
        Assert.Equal(ErrorCodes.BadRequest, (await Fail(h, """{"id":1,"method":"init","params":{}}""")).Code);
        Assert.Equal(ErrorCodes.BadRequest, (await Fail(h, """{"v":2,"id":1,"method":"init","params":{}}""")).Code);
        Assert.Equal(ErrorCodes.BadRequest, (await Fail(h, """{"v":1,"id":"x","method":"init","params":{}}""")).Code);
        Assert.Equal(ErrorCodes.BadRequest, (await Fail(h, """{"v":1,"id":1,"params":{}}""")).Code);
        Assert.Equal(ErrorCodes.BadRequest, (await Fail(h, """{"v":1,"id":1,"method":"init"}""")).Code);

        var unknown = await Fail(h, """{"v":1,"id":9,"method":"formatla","params":{}}""");
        Assert.Equal(ErrorCodes.UnknownMethod, unknown.Code);
        Assert.Equal(9, unknown.Id.GetInt32());
    }

    [Fact]
    public async Task BadParameters_AreRejectedBeforeAnythingRuns()
    {
        using var h = new BridgeHarness();
        h.Files.File("a.txt");

        var missing = await Fail(h, """{"v":1,"id":3,"method":"scan","params":{"folder":"C:\\x"}}""");
        Assert.Equal(ErrorCodes.BadRequest, missing.Code);
        Assert.Contains("options", missing.Message);

        var wrongType = await Fail(h, """{"v":1,"id":4,"method":"moveFavorite","params":{"id":"a","delta":"bir"}}""");
        Assert.Contains("delta", wrongType.Message);

        var badEnum = await Fail(h, """{"v":1,"id":5,"method":"executeRename","params":{"decision":"belki","description":"x"}}""");
        Assert.Contains("decision", badEnum.Message);

        var extra = await Fail(h, """{"v":1,"id":6,"method":"init","params":{"gizli":true}}""");
        Assert.Contains("gizli", extra.Message);
    }

    [Fact]
    public async Task Responses_AreWellFormedEnvelopes_AndTurkishLettersAreNotEscaped()
    {
        using var h = new BridgeHarness();
        h.Files.File("Öğrenci Şeması.docx");

        string raw = await h.Dispatcher.HandleAsync(
            JsonSerializer.Serialize(new { v = 1, id = 41, method = "scan", @params = new ScanParams(h.Root, BridgeHarness.AllFiles) }, Wire.Options));

        Assert.Contains("Öğrenci Şeması.docx", raw);
        Assert.StartsWith("{\"v\":1,\"id\":41,\"result\":", raw);
        Assert.DoesNotContain("\\u", raw);
    }
}
