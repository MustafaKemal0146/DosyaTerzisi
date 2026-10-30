using System.IO;
using DosyaTerzisi.Core;

namespace DosyaTerzisi.Shell;

/// <summary>Programın nerede ne saklayacağı ve nasıl açıldığı: komut satırı ve ortam değişkenleri.</summary>
public sealed class ShellOptions
{
    /// <summary>Başlangıçta listelenecek klasör (komut satırındaki ilk klasör; "Birlikte aç" ile gelir).</summary>
    public string InitialFolder { get; init; } = "";

    /// <summary>
    /// Komut satırından verilen dosyalar ve klasörler (Gezgin'de "Gönder" menüsü): birden çoksa ya da dosyaysa liste bunlardır.
    /// Tek bir klasör verildiyse <see cref="InitialFolder"/> dolar, burası boş kalır.
    /// </summary>
    public IReadOnlyList<string> InitialPaths { get; init; } = [];

    /// <summary>
    /// Arayüzü gömülü dosyalar yerine bu adresten yükler (<c>TERZI_UI_URL=http://localhost:3100</c>). Yalnızca geliştirme içindir:
    /// sayfa yenilenir, geliştirici araçları açılır.
    /// </summary>
    public string? DevUrl { get; init; }

    /// <summary>Geçmiş, favoriler ve tarayıcı profilinin saklandığı klasör. Varsayılan: AppData. <c>TERZI_DATA_DIR</c> ile değişir.</summary>
    public string? DataDir { get; init; }

    public string HistoryPath => DataDir is null ? HistoryStore.DefaultPath : Path.Combine(DataDir, "history.json");

    public string FavoritesPath => DataDir is null ? FavoriteStore.DefaultPath : Path.Combine(DataDir, "favorites.json");

    public string WebViewProfile => Path.Combine(
        DataDir ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DosyaTerzisi"),
        "WebView2");

    public string PresetFile => Path.Combine(AppContext.BaseDirectory, "Presets", "OnAyarlar.txt");

    /// <summary>
    /// Deneme için: pencere ekranın dışında, odak almadan ve görev çubuğuna girmeden açılır (<c>TERZI_OFFSCREEN=1</c>).
    /// Kullanıcının ekranına hiçbir şey çıkmadan otomatik denemeler yapılabilsin diye.
    /// </summary>
    public bool Offscreen { get; init; }

    public bool IsDevelopment => DevUrl is not null;

    public static ShellOptions From(string[] args, Func<string, string?> environment)
    {
        var existing = args
            .Where(a => !a.StartsWith('-') && (Directory.Exists(a) || File.Exists(a)))
            .Select(Path.GetFullPath)
            .ToList();
        string? folder = existing.Count == 1 && Directory.Exists(existing[0]) ? existing[0] : null;
        string? dev = environment("TERZI_UI_URL");
        string? data = environment("TERZI_DATA_DIR");

        return new ShellOptions
        {
            InitialFolder = folder ?? "",
            InitialPaths = folder is null ? existing : [],
            DevUrl = string.IsNullOrWhiteSpace(dev) ? null : dev,
            DataDir = string.IsNullOrWhiteSpace(data) ? null : Path.GetFullPath(data),
            Offscreen = environment("TERZI_OFFSCREEN") == "1",
        };
    }
}
