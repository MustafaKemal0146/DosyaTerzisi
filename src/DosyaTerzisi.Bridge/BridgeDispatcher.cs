using System.Text.Json;
using DosyaTerzisi.Bridge.Contract;

namespace DosyaTerzisi.Bridge;

/// <summary>
/// Arayüzden gelen JSON istek mesajını alır, sözleşmeye uyup uymadığını denetler, ilgili işi <see cref="BridgeSession"/>'a
/// yaptırır ve cevap mesajını (JSON) verir. Hiçbir hata dışarı sızmaz: her şey bir hata cevabına çevrilir.
/// </summary>
public sealed class BridgeDispatcher(BridgeSession session)
{
    private readonly ContractValidator _validator = ContractValidator.Default;

    // Oturum tek iş parçacığı gibi çalışır: arayüz liste yenilerken önizleme isteği gelirse sıraya girer.
    private readonly SemaphoreSlim _one = new(1, 1);

    /// <summary>İsteği işler. Aynı anda gelen istekler sırayla yapılır.</summary>
    public async Task<string> HandleAsync(string requestJson)
    {
        await _one.WaitAsync();
        try
        {
            return await HandleCoreAsync(requestJson);
        }
        finally
        {
            _one.Release();
        }
    }

    private async Task<string> HandleCoreAsync(string requestJson)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(requestJson);
        }
        catch (JsonException ex)
        {
            return Error(null, ErrorCodes.BadRequest, $"İstek JSON değil: {ex.Message}");
        }

        using (document)
        {
            var root = document.RootElement;
            long? id = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("id", out var idElement) && idElement.ValueKind == JsonValueKind.Number && idElement.TryGetInt64(out long parsed)
                ? parsed
                : null;

            if (root.ValueKind != JsonValueKind.Object) return Error(id, ErrorCodes.BadRequest, "İstek bir nesne olmalı.");
            if (!root.TryGetProperty("v", out var version) || version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out int v) || v != Wire.Version)
                return Error(id, ErrorCodes.BadRequest, $"Desteklenen sürüm: {Wire.Version}.");
            if (id is null) return Error(null, ErrorCodes.BadRequest, "İstekte tam sayı bir id olmalı.");
            if (!root.TryGetProperty("method", out var methodElement) || methodElement.ValueKind != JsonValueKind.String)
                return Error(id, ErrorCodes.BadRequest, "İstekte method olmalı.");

            string method = methodElement.GetString()!;
            if (Catalog.Find(method) is not { } spec) return Error(id, ErrorCodes.UnknownMethod, $"Bilinmeyen iş: {method}");

            if (!root.TryGetProperty("params", out var parameters)) return Error(id, ErrorCodes.BadRequest, "İstekte params olmalı.");
            var problems = _validator.ValidateParams(method, parameters);
            if (problems.Count > 0) return Error(id, ErrorCodes.BadRequest, $"{method}: {string.Join("; ", problems.Take(5))}");

            try
            {
                object? arguments = JsonSerializer.Deserialize(parameters.GetRawText(), spec.Params, Wire.Options);
                object? result = await InvokeAsync(method, arguments!);
                return Result(id.Value, JsonSerializer.Serialize(result, spec.Result, Wire.Options));
            }
            catch (BridgeException ex)
            {
                return Error(id, ex.Code, ex.Message);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return Error(id, ErrorCodes.Failed, ex.Message);
            }
            catch (Exception ex)
            {
                return Error(id, ErrorCodes.Internal, $"{ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    private async Task<object?> InvokeAsync(string method, object p) => method switch
    {
        "init" => await session.InitAsync(),
        "pickFolder" => await session.PickFolderAsync((PickFolderParams)p),
        "scan" => await session.ScanAsync((ScanParams)p),
        "preview" => await session.PreviewAsync((PreviewParams)p),
        "prepareRename" => await session.PrepareRenameAsync(),
        "executeRename" => await session.ExecuteRenameAsync((ExecuteRenameParams)p),
        "undo" => await session.UndoAsync(),
        "history" => await session.HistoryAsync(),
        "addFavorite" => await session.AddFavoriteAsync((AddFavoriteParams)p),
        "updateFavorite" => await session.UpdateFavoriteAsync((UpdateFavoriteParams)p),
        "removeFavorite" => await session.RemoveFavoriteAsync((RemoveFavoriteParams)p),
        "moveFavorite" => await session.MoveFavoriteAsync((MoveFavoriteParams)p),
        "importFavorites" => await session.ImportFavoritesAsync(),
        "exportFavorites" => await session.ExportFavoritesAsync(),
        "refresh" => await session.RefreshAsync(),
        "openPaths" => await session.OpenPathsAsync((OpenPathsParams)p),
        "listFolders" => await session.ListFoldersAsync((ListFoldersParams)p),
        "pickFile" => await session.PickFileAsync((PickFileParams)p),
        "shellIntegration" => session.ShellIntegration(),
        "setShellIntegration" => session.SetShellIntegration((SetShellIntegrationParams)p),
        "removeRows" => await session.RemoveRowsAsync((RemoveRowsParams)p),
        "tagPreview" => await session.TagPreviewAsync((TagPreviewParams)p),
        "scriptInfo" => await session.ScriptInfoAsync(),
        "editScript" => await session.EditScriptAsync(),
        "tagColumns" => await session.TagColumnsAsync((TagColumnsParams)p),
        "readTags" => await session.ReadTagsAsync((ReadTagsParams)p),
        "attributePreview" => await session.AttributePreviewAsync((AttributePreviewParams)p),
        "applyAttributes" => await session.ApplyAttributesAsync((AttributePreviewParams)p),
        "undoAttributes" => await session.UndoAttributesAsync(),
        "numberedPreview" => await session.NumberedPreviewAsync((NumberedOptions)p),
        "createNumbered" => await session.CreateNumberedAsync((NumberedOptions)p),
        _ => throw new BridgeException(ErrorCodes.UnknownMethod, $"Bilinmeyen iş: {method}"),
    };

    private static string Result(long id, string resultJson) => $"{{\"v\":{Wire.Version},\"id\":{id},\"result\":{resultJson}}}";

    private static string Error(long? id, string code, string message) =>
        $"{{\"v\":{Wire.Version},\"id\":{(id?.ToString() ?? "null")},\"error\":{{\"code\":{JsonSerializer.Serialize(code)},\"message\":{JsonSerializer.Serialize(message, Wire.Options)}}}}}";
}
