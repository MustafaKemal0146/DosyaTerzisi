namespace DosyaTerzisi.Bridge;

/// <summary>Windows'un kendi pencereleri: klasör ve dosya seçiciler. Kabuk (WebView2 penceresi) uygular.</summary>
public interface INativeDialogs
{
    /// <returns>Seçilen klasör, vazgeçildiyse null.</returns>
    Task<string?> PickFolderAsync(string? initialFolder);

    /// <param name="filter">Windows dosya penceresi süzgeci, örneğin <c>Metin|*.txt|Tümü|*.*</c>.</param>
    Task<string?> PickOpenFileAsync(string title, string filter);

    Task<string?> PickSaveFileAsync(string title, string filter, string defaultName);

    /// <summary>Dosyayı varsayılan metin düzenleyicide (yoksa Not Defteri'nde) açar; düzenleyicinin kapanmasını beklemez.</summary>
    Task OpenInEditorAsync(string path);
}
