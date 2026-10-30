using System.Text.Json;
using System.Text.Json.Serialization;

namespace DosyaTerzisi.Bridge.Contract;

/// <summary>Tellerde dolaşan JSON'un biçimi.</summary>
public static class Wire
{
    /// <summary>
    /// Mesaj zarfının sürümü. Zarf: <c>{"v":1,"id":7,"method":"scan","params":{...}}</c> isteği,
    /// <c>{"v":1,"id":7,"result":...}</c> cevabı, <c>{"v":1,"id":7,"error":{"code":"...","message":"..."}}</c> hatayı gösterir.
    /// </summary>
    public const int Version = 1;

    public static readonly JsonSerializerOptions Options = Create(indented: false);

    /// <summary>Örnek dosyalarının okunaklı olması için girintili.</summary>
    public static readonly JsonSerializerOptions Indented = Create(indented: true);

    private static JsonSerializerOptions Create(bool indented) => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        WriteIndented = indented,
        // Türkçe harfler olduğu gibi yazılsın (Ö değil).
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
}

/// <summary>Hata cevabındaki <c>code</c> değerleri.</summary>
public static class ErrorCodes
{
    /// <summary>İstek biçimi ya da parametreleri sözleşmeye uymuyor.</summary>
    public const string BadRequest = "badRequest";

    public const string UnknownMethod = "unknownMethod";

    /// <summary>İş yapılamadı (klasör yok, dosya kilitli...). Mesaj kullanıcıya gösterilebilir.</summary>
    public const string Failed = "failed";

    /// <summary>Beklenmeyen bir hata.</summary>
    public const string Internal = "internal";
}

/// <summary>Kullanıcıya gösterilebilecek bir mesajla biten iş hatası.</summary>
public sealed class BridgeException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;

    public static BridgeException Failed(string message) => new(ErrorCodes.Failed, message);

    public static BridgeException BadRequest(string message) => new(ErrorCodes.BadRequest, message);
}
