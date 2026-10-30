using System.Text;
using DosyaTerzisi.Bridge;

namespace DosyaTerzisi.Tests;

public class SiteTests
{
    private static readonly SiteServer Server = new(new AssemblySiteContent(typeof(SiteTests).Assembly));

    private static SiteResponse Get(string path, string method = "GET") =>
        Server.Handle(method, new Uri($"https://app.dosyaterzisi{path}"));

    private static string Text(SiteResponse response) => Encoding.UTF8.GetString(response.Body);

    // ---- Gömülü dosyalar -----------------------------------------------------------------------------------------

    [Fact]
    public void EmbeddedFiles_AreFound_WithSlashesInThePaths()
    {
        var content = new AssemblySiteContent(typeof(SiteTests).Assembly);

        Assert.True(content.Count >= 4);
        Assert.NotNull(content.TryRead("index.html"));
        Assert.NotNull(content.TryRead("_next/static/app.js"));
        Assert.Null(content.TryRead("yok.html"));
        Assert.Null(content.TryRead("_next\\static\\app.js"));
    }

    [Fact]
    public void ARootThatHasNothingEmbedded_IsEmpty()
    {
        Assert.Equal(0, new AssemblySiteContent(typeof(SiteTests).Assembly, "baska/").Count);
    }

    // ---- Sunma ---------------------------------------------------------------------------------------------------

    [Fact]
    public void Root_ServesTheIndexPage_WithSecurityHeaders()
    {
        var response = Get("/");

        Assert.Equal(200, response.Status);
        Assert.Equal("text/html; charset=utf-8", response.ContentType);
        Assert.Contains("Merhaba Öğrenci", Text(response));
        Assert.Equal(SiteServer.ContentSecurityPolicy, response.Headers["Content-Security-Policy"]);
        Assert.Equal("nosniff", response.Headers["X-Content-Type-Options"]);
        Assert.Equal("no-cache", response.Headers["Cache-Control"]);
        Assert.Contains("Content-Type: text/html; charset=utf-8", response.HeaderText);
        Assert.Contains("\r\n", response.HeaderText);
    }

    [Theory]
    [InlineData("/_next/static/app.js", "text/javascript; charset=utf-8")]
    [InlineData("/_next/static/app.css", "text/css; charset=utf-8")]
    [InlineData("/index.html", "text/html; charset=utf-8")]
    public void Files_GetTheirContentType(string path, string type)
    {
        var response = Get(path);

        Assert.Equal(200, response.Status);
        Assert.Equal(type, response.ContentType);
    }

    [Fact]
    public void ScriptsAndStyles_DoNotCarryThePageSecurityPolicy()
    {
        Assert.False(Get("/_next/static/app.js").Headers.ContainsKey("Content-Security-Policy"));
    }

    [Fact]
    public void APageWithoutTheExtension_FindsItsHtmlFile()
    {
        var response = Get("/ayarlar");

        Assert.Equal(200, response.Status);
        Assert.Contains("Ayarlar", Text(response));
    }

    [Fact]
    public void Query_AndEncodedLetters_AreHandled()
    {
        Assert.Equal(200, Get("/index.html?big=5000").Status);
        Assert.Equal(200, Get("/_next/static/%61pp.js").Status);
    }

    [Fact]
    public void Head_HasNoBody_ButSameHeaders()
    {
        var response = Get("/", "HEAD");

        Assert.Equal(200, response.Status);
        Assert.Empty(response.Body);
        Assert.Equal("text/html; charset=utf-8", response.ContentType);
    }

    // ---- Reddedilenler -------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("/yok.js")]
    [InlineData("/_next/static/")]
    [InlineData("/../secret.txt")]
    [InlineData("/_next/../../secret.txt")]
    [InlineData("/%2e%2e/secret.txt")]
    [InlineData("/_next%2f..%2f..%2fsecret.txt")]
    [InlineData("/_next/static%5capp.js")]
    [InlineData("/C:/Windows/win.ini")]
    [InlineData("/%5cwindows")]
    public void UnknownOrDangerousPaths_Get404(string path)
    {
        var response = Get(path);

        Assert.Equal(404, response.Status);
        Assert.Equal("text/plain; charset=utf-8", response.ContentType);
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    public void OnlyReadingIsAllowed(string method) => Assert.Equal(405, Get("/", method).Status);

    [Fact]
    public void OtherHosts_AndPlainHttp_AreNotServed()
    {
        Assert.Equal(404, Server.Handle("GET", new Uri("https://example.com/")).Status);
        Assert.Equal(404, Server.Handle("GET", new Uri("http://app.dosyaterzisi/")).Status);
    }

    // ---- Gezinme kuralı ------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("https://app.dosyaterzisi/", true)]
    [InlineData("https://app.dosyaterzisi/index.html?big=5", true)]
    [InlineData("https://APP.dosyaterzisi/x", true)]
    [InlineData("about:blank", true)]
    [InlineData("https://example.com/", false)]
    [InlineData("https://app.dosyaterzisi.evil.com/", false)]
    [InlineData("http://app.dosyaterzisi/", false)]
    [InlineData("https://app.dosyaterzisi:8443/", false)]
    [InlineData("file:///C:/Windows/win.ini", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("data:text/html,<b>x</b>", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("kesinlikle bir adres değil", false)]
    public void Navigation_IsLimitedToTheAppPage(string? url, bool allowed) =>
        Assert.Equal(allowed, new NavigationPolicy(SiteServer.Origin()).IsAllowed(url));

    [Fact]
    public void Navigation_CanAlsoAllowTheDevelopmentServer()
    {
        var policy = new NavigationPolicy(SiteServer.Origin(), "http://localhost:3100");

        Assert.True(policy.IsAllowed("http://localhost:3100/?big=5000"));
        Assert.False(policy.IsAllowed("http://localhost:3101/"));
        Assert.False(policy.IsAllowed("http://localhost/"));
        Assert.True(policy.IsAllowed("https://app.dosyaterzisi/"));
        Assert.False(new NavigationPolicy(SiteServer.Origin(), "bozuk adres").IsAllowed("http://localhost:3100/"));
    }

    // ---- Sıralı çalışma ------------------------------------------------------------------------------------------

    [Fact]
    public async Task Dispatcher_RunsOneRequestAtATime()
    {
        using var h = new BridgeHarness();
        h.Files.File("a.txt");
        string scan = System.Text.Json.JsonSerializer.Serialize(
            new { v = 1, id = 1, method = "scan", @params = new DosyaTerzisi.Bridge.Contract.ScanParams(h.Root, BridgeHarness.AllFiles) },
            DosyaTerzisi.Bridge.Contract.Wire.Options);
        string preview = System.Text.Json.JsonSerializer.Serialize(
            new
            {
                v = 1, id = 2, method = "preview",
                @params = new DosyaTerzisi.Bridge.Contract.PreviewParams(BridgeHarness.Rule(DosyaTerzisi.Bridge.Contract.RenameMethod.Wildcard, "*", @"x_\1"), [], []),
            },
            DosyaTerzisi.Bridge.Contract.Wire.Options);

        // Önizleme isteği, listeleme bitmeden gelse bile listeleme bittikten sonra çalışır: satır görür.
        var first = h.Dispatcher.HandleAsync(scan);
        var second = h.Dispatcher.HandleAsync(preview);
        var responses = await Task.WhenAll(first, second);

        using var document = System.Text.Json.JsonDocument.Parse(responses[1]);
        Assert.Equal(1, document.RootElement.GetProperty("result").GetProperty("rows").GetArrayLength());
    }
}

public class DroppedPathsTests
{
    private const string Request = """{"v":1,"id":4,"method":"openPaths","params":{"paths":[]}}""";

    [Fact]
    public void PathsFromWebView2_AreWrittenIntoOpenPaths()
    {
        string json = DosyaTerzisi.Bridge.DroppedPaths.Inject(Request, [@"C:\Belgeler\Öğrenci.docx", @"C:\Belgeler\a.txt"]);

        using var document = System.Text.Json.JsonDocument.Parse(json);
        var paths = document.RootElement.GetProperty("params").GetProperty("paths").EnumerateArray().Select(p => p.GetString()).ToList();
        Assert.Equal([@"C:\Belgeler\Öğrenci.docx", @"C:\Belgeler\a.txt"], paths);
        Assert.Equal(4, document.RootElement.GetProperty("id").GetInt32());
    }

    [Fact]
    public void PathsTheInterfaceWroteItself_AreReplaced()
    {
        string json = DosyaTerzisi.Bridge.DroppedPaths.Inject(
            """{"v":1,"id":4,"method":"openPaths","params":{"paths":["C:/Windows/win.ini"]}}""", [@"C:\gercek.txt"]);

        Assert.DoesNotContain("win.ini", json);
        Assert.Contains("gercek.txt", json);
    }

    [Theory]
    [InlineData("""{"v":1,"id":4,"method":"scan","params":{"folder":"C:/x","options":{}}}""")]
    [InlineData("""{"v":1,"id":4,"method":"createNumbered","params":{}}""")]
    [InlineData("""{"v":1,"id":4,"method":"openPaths"}""")]
    [InlineData("""bu json değil""")]
    [InlineData("""[1,2]""")]
    public void NoOtherRequestGetsPaths(string request) =>
        Assert.Equal(request, DosyaTerzisi.Bridge.DroppedPaths.Inject(request, [@"C:\x.txt"]));

    [Fact]
    public void WithoutDroppedFiles_NothingChanges() =>
        Assert.Equal(Request, DosyaTerzisi.Bridge.DroppedPaths.Inject(Request, []));

    [Fact]
    public async Task OpenPathsWorksEndToEnd_ThroughTheInjection()
    {
        using var h = new BridgeHarness();
        h.Files.File("a.txt");
        h.Files.File("b.txt");
        string request = DosyaTerzisi.Bridge.DroppedPaths.Inject(Request, [Path.Combine(h.Root, "b.txt")]);

        using var response = System.Text.Json.JsonDocument.Parse(await h.Dispatcher.HandleAsync(request));

        var rows = response.RootElement.GetProperty("result").GetProperty("list").GetProperty("rows");
        Assert.Equal(1, rows.GetArrayLength());
        Assert.Equal("b.txt", rows[0].GetProperty("name").GetString());
    }
}
