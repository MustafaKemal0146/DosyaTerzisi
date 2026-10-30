using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using DosyaTerzisi.Bridge;
using DosyaTerzisi.Core;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;

namespace DosyaTerzisi.Shell;

/// <summary>
/// Uygulama penceresi: içinde yalnızca WebView2 vardır. Arayüz (Next.js çıktısı) exe'nin içine gömülüdür ve
/// <c>https://app.dosyaterzisi/</c> adresinden sunulur; internet gerekmez. Arayüzün istekleri JSON mesajı olarak gelir,
/// <see cref="BridgeDispatcher"/> işler, cevap yine mesajla döner.
/// </summary>
public partial class MainWindow : Window
{
    private const string RuntimeDownloadUrl = "https://go.microsoft.com/fwlink/p/?LinkId=2124703";

    private readonly ShellOptions _options;
    private readonly SiteServer _site;
    private readonly NavigationPolicy _navigation;
    private readonly BridgeDispatcher _dispatcher;

    public MainWindow()
    {
        InitializeComponent();

        _options = ShellOptions.From(Environment.GetCommandLineArgs().Skip(1).ToArray(), Environment.GetEnvironmentVariable);
        _site = new SiteServer(new AssemblySiteContent(typeof(MainWindow).Assembly));
        _navigation = new NavigationPolicy(SiteServer.Origin(), _options.DevUrl);

        var session = new BridgeSession(
            new RenameService(new HistoryStore(_options.HistoryPath)),
            new FavoriteStore(_options.FavoritesPath),
            new NativeDialogs(this),
            _options.PresetFile,
            _options.InitialFolder,
            _options.InitialPaths,
            _options.IsDevelopment || _options.DataDir is not null ? null : new ShellIntegration(Environment.ProcessPath ?? ""),
            _options.DataDir);
        _dispatcher = new BridgeDispatcher(session);

        if (_options.Offscreen)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = -32000;
            Top = -32000;
            ShowActivated = false;
            ShowInTaskbar = false;
        }

        ApplyTheme();
        Loaded += async (_, _) => await StartAsync();
    }

    private async Task StartAsync()
    {
        CoreWebView2Environment environment;
        try
        {
            Directory.CreateDirectory(_options.WebViewProfile);
            environment = await CoreWebView2Environment.CreateAsync(null, _options.WebViewProfile);
            await Web.EnsureCoreWebView2Async(environment);
        }
        catch (WebView2RuntimeNotFoundException)
        {
            OfferRuntimeDownload();
            Close();
            return;
        }

        var core = Web.CoreWebView2;
        bool dev = _options.IsDevelopment;

        var settings = core.Settings;
        settings.AreDefaultContextMenusEnabled = dev;
        settings.AreDevToolsEnabled = dev;
        settings.AreBrowserAcceleratorKeysEnabled = dev;
        settings.IsBuiltInErrorPageEnabled = dev;
        settings.IsStatusBarEnabled = false;
        settings.IsZoomControlEnabled = false;
        settings.IsSwipeNavigationEnabled = false;
        settings.IsGeneralAutofillEnabled = false;
        settings.IsPasswordAutosaveEnabled = false;
        settings.AreHostObjectsAllowed = false;

        // Uygulamanın kendi sayfası dışına çıkış yok: gezinme, yeni pencere, izin ve indirme engellenir.
        core.NavigationStarting += (_, e) => e.Cancel = !_navigation.IsAllowed(e.Uri);
        core.FrameNavigationStarting += (_, e) => e.Cancel = !_navigation.IsAllowed(e.Uri);
        core.NewWindowRequested += (_, e) => e.Handled = true;
        core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
        core.DownloadStarting += (_, e) => e.Cancel = true;
        core.ProcessFailed += (_, e) => OnProcessFailed(e);

        // Gömülü dosyaları sanal adresten sun.
        core.AddWebResourceRequestedFilter($"{SiteServer.Origin()}/*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += (_, e) => ServeSite(environment, e);

        core.WebMessageReceived += async (_, e) => await OnMessageAsync(e);

        core.Navigate(_options.DevUrl ?? SiteServer.Origin() + "/");
    }

    private void ServeSite(CoreWebView2Environment environment, CoreWebView2WebResourceRequestedEventArgs e)
    {
        SiteResponse response;
        try
        {
            response = _site.Handle(e.Request.Method, new Uri(e.Request.Uri));
        }
        catch (UriFormatException)
        {
            response = _site.Handle("GET", new Uri(SiteServer.Origin() + "/yok"));
        }

        e.Response = environment.CreateWebResourceResponse(
            new MemoryStream(response.Body), response.Status, response.Reason, response.HeaderText);
    }

    private async Task OnMessageAsync(CoreWebView2WebMessageReceivedEventArgs e)
    {
        // Yalnızca kendi sayfamızdan gelen mesajlar işlenir.
        if (!_navigation.IsAllowed(e.Source)) return;

        string? request;
        try
        {
            request = e.TryGetWebMessageAsString();
        }
        catch (ArgumentException)
        {
            return; // Metin olmayan mesaj: bizim protokolümüz değil.
        }
        if (request is null) return;

        // Sürüklenen dosyalar: yolları yalnızca WebView2 bilir, mesajın yanında gelir.
        var dropped = e.AdditionalObjects?.OfType<CoreWebView2File>().Select(f => f.Path).ToList();
        if (dropped is { Count: > 0 }) request = DroppedPaths.Inject(request, dropped);

        string response = await _dispatcher.HandleAsync(request);

        try
        {
            Web.CoreWebView2?.PostWebMessageAsString(response);
        }
        catch (InvalidOperationException)
        {
            // Pencere kapanırken gelen cevap: gösterecek yer kalmadı.
        }
    }

    private void OnProcessFailed(CoreWebView2ProcessFailedEventArgs e)
    {
        // Arayüz süreci çöktüyse sayfayı yeniden yükle; kullanıcının dosyalarına dokunulmaz.
        if (e.ProcessFailedKind is CoreWebView2ProcessFailedKind.RenderProcessExited or CoreWebView2ProcessFailedKind.RenderProcessUnresponsive)
            Web.CoreWebView2.Reload();
    }

    /// <summary>Pencere, arayüz yüklenene kadar sistem temasının rengini taşır (beyaz parlama olmasın).</summary>
    private void ApplyTheme()
    {
        bool light = Registry.GetValue(
            @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1) is not 0;
        var color = light ? Color.FromRgb(0xF3, 0xF3, 0xF3) : Color.FromRgb(0x20, 0x20, 0x20);

        Background = new SolidColorBrush(color);
        Web.DefaultBackgroundColor = System.Drawing.Color.FromArgb(color.R, color.G, color.B);
    }

    private void OfferRuntimeDownload()
    {
        var answer = MessageBox.Show(
            "Dosya Terzisi, Microsoft Edge WebView2 bileşenine ihtiyaç duyar; bu bilgisayarda kurulu değil.\n\n" +
            "İndirme sayfasını açayım mı? (Kurduktan sonra programı yeniden açın.)",
            "Dosya Terzisi", MessageBoxButton.YesNo, MessageBoxImage.Information);

        if (answer == MessageBoxResult.Yes)
            Process.Start(new ProcessStartInfo(RuntimeDownloadUrl) { UseShellExecute = true });
    }
}
