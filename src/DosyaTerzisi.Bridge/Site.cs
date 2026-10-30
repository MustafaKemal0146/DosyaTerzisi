using System.Reflection;

namespace DosyaTerzisi.Bridge;

/// <summary>Gömülü web sitesinin dosyaları. Yoldan (<c>_next/static/app.js</c>) içeriğe.</summary>
public interface ISiteContent
{
    /// <returns>Dosyanın baytları; dosya yoksa null.</returns>
    byte[]? TryRead(string path);
}

/// <summary>Bir derlemeye (assembly) gömülü kaynaklardan sunar. Kaynak adı: <c>web/_next/static/app.js</c>.</summary>
public sealed class AssemblySiteContent : ISiteContent
{
    private readonly Assembly _assembly;
    private readonly Dictionary<string, string> _names = new(StringComparer.Ordinal);

    public AssemblySiteContent(Assembly assembly, string prefix = "web/")
    {
        _assembly = assembly;
        foreach (var name in assembly.GetManifestResourceNames())
        {
            if (!name.StartsWith(prefix, StringComparison.Ordinal)) continue;

            // MSBuild Windows'ta ters bölü ile yazabilir; adres her zaman düz bölüdür.
            _names[name[prefix.Length..].Replace('\\', '/')] = name;
        }
    }

    public int Count => _names.Count;

    public byte[]? TryRead(string path)
    {
        if (!_names.TryGetValue(path, out var resource)) return null;

        using var stream = _assembly.GetManifestResourceStream(resource);
        if (stream is null) return null;

        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}

public sealed record SiteResponse(int Status, string Reason, string ContentType, byte[] Body, IReadOnlyDictionary<string, string> Headers)
{
    /// <summary>WebView2'nin beklediği biçim: <c>Ad: değer</c> satırları.</summary>
    public string HeaderText => string.Join("\r\n", Headers.Select(h => $"{h.Key}: {h.Value}"));
}

/// <summary>
/// Gömülü siteyi sanal bir adresten (<c>https://app.dosyaterzisi/</c>) sunar. Yalnızca okuma (GET/HEAD), yalnızca kendi
/// dosyaları; üst klasöre çıkmaya çalışan ya da bilinmeyen adresler 404 alır.
/// </summary>
public sealed class SiteServer(ISiteContent content, string host = SiteServer.DefaultHost)
{
    public const string DefaultHost = "app.dosyaterzisi";

    public static string Origin(string host = DefaultHost) => $"https://{host}";

    /// <summary>Sayfaların yalnızca kendi kaynaklarından yüklenmesine izin verir; dışarıya bağlanmak yok.</summary>
    public const string ContentSecurityPolicy =
        "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; " +
        "font-src 'self' data:; connect-src 'self'; object-src 'none'; base-uri 'none'; frame-ancestors 'none'; form-action 'none'";

    private static readonly Dictionary<string, string> ContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".html"] = "text/html; charset=utf-8",
        [".js"] = "text/javascript; charset=utf-8",
        [".mjs"] = "text/javascript; charset=utf-8",
        [".css"] = "text/css; charset=utf-8",
        [".json"] = "application/json; charset=utf-8",
        [".txt"] = "text/plain; charset=utf-8",
        [".svg"] = "image/svg+xml",
        [".png"] = "image/png",
        [".ico"] = "image/x-icon",
        [".jpg"] = "image/jpeg",
        [".webp"] = "image/webp",
        [".woff"] = "font/woff",
        [".woff2"] = "font/woff2",
        [".map"] = "application/json",
    };

    public SiteResponse Handle(string method, Uri uri)
    {
        if (!string.Equals(uri.Host, host, StringComparison.OrdinalIgnoreCase) || uri.Scheme != Uri.UriSchemeHttps)
            return Plain(404, "Not Found", "Bulunamadı.");
        if (method is not ("GET" or "HEAD"))
            return Plain(405, "Method Not Allowed", "Bu adres yalnızca okunabilir.");

        string? path = Normalize(uri);
        if (path is null) return Plain(404, "Not Found", "Bulunamadı.");

        // Next dışa aktarması "/ayarlar" için "ayarlar.html" üretir; "/" için index.html.
        byte[]? body = null;
        foreach (var candidate in new[] { path, path + ".html", path.TrimEnd('/') + "/index.html" })
        {
            body = content.TryRead(candidate);
            if (body is not null)
            {
                path = candidate;
                break;
            }
        }
        if (body is null) return Plain(404, "Not Found", "Bulunamadı.");

        string type = ContentTypes.GetValueOrDefault(Path.GetExtension(path), "application/octet-stream");
        var headers = new Dictionary<string, string>
        {
            ["Content-Type"] = type,
            ["Cache-Control"] = "no-cache",
            ["X-Content-Type-Options"] = "nosniff",
        };
        if (type.StartsWith("text/html", StringComparison.Ordinal)) headers["Content-Security-Policy"] = ContentSecurityPolicy;

        return new SiteResponse(200, "OK", type, method == "HEAD" ? [] : body, headers);
    }

    /// <summary>Adresin yolunu dosya yoluna çevirir. Üst klasöre çıkma ve garip karakterler reddedilir (null).</summary>
    internal static string? Normalize(Uri uri)
    {
        string path = Uri.UnescapeDataString(uri.AbsolutePath).TrimStart('/');
        if (path.Length == 0) return "index.html";

        foreach (var segment in path.Split('/'))
        {
            if (segment is ".." or "." || segment.Contains('\\') || segment.Contains(':') || segment.Any(char.IsControl))
                return null;
        }
        return path;
    }

    private static SiteResponse Plain(int status, string reason, string text) => new(
        status,
        reason,
        "text/plain; charset=utf-8",
        System.Text.Encoding.UTF8.GetBytes(text),
        new Dictionary<string, string> { ["Content-Type"] = "text/plain; charset=utf-8", ["X-Content-Type-Options"] = "nosniff" });
}

/// <summary>Pencerenin hangi adreslere gidebileceği. Uygulamanın kendi sayfası dışındaki her şey engellenir.</summary>
public sealed class NavigationPolicy
{
    private readonly List<Uri> _allowed = [];

    public NavigationPolicy(string origin, string? devUrl = null)
    {
        _allowed.Add(new Uri(origin));
        if (devUrl is not null && Uri.TryCreate(devUrl, UriKind.Absolute, out var dev)) _allowed.Add(dev);
    }

    public bool IsAllowed(string? url)
    {
        if (string.IsNullOrEmpty(url)) return false;
        if (url == "about:blank") return true;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;

        return _allowed.Any(a =>
            string.Equals(a.Scheme, uri.Scheme, StringComparison.OrdinalIgnoreCase)
            && string.Equals(a.Host, uri.Host, StringComparison.OrdinalIgnoreCase)
            && a.Port == uri.Port);
    }
}
