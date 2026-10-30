using System.Text.Json;
using System.Text.Json.Nodes;

namespace DosyaTerzisi.Bridge;

/// <summary>
/// Web arayüzü, sürüklenen dosyaların yollarını göremez; WebView2 bunları mesajın yanında uygulamaya ayrıca verir.
/// Kabuk bu yolları <c>openPaths</c> isteğinin <c>params.paths</c> alanına yazar. Başka hiçbir isteğe yol eklenmez:
/// arayüz, kendi isteğiyle dosya yolu uyduramasın.
/// </summary>
public static class DroppedPaths
{
    public static string Inject(string requestJson, IReadOnlyList<string> paths)
    {
        if (paths.Count == 0) return requestJson;

        JsonNode? node;
        try
        {
            node = JsonNode.Parse(requestJson);
        }
        catch (JsonException)
        {
            return requestJson;
        }

        if (node is not JsonObject request
            || request["method"]?.GetValue<string>() != "openPaths"
            || request["params"] is not JsonObject parameters)
            return requestJson;

        // Arayüzün kendi yazdığı yollar atılır; yalnızca WebView2'nin verdikleri geçerlidir.
        parameters["paths"] = new JsonArray(paths.Select(p => (JsonNode?)JsonValue.Create(p)).ToArray());
        return request.ToJsonString();
    }
}
