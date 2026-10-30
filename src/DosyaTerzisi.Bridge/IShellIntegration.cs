using DosyaTerzisi.Bridge.Contract;

namespace DosyaTerzisi.Bridge;

/// <summary>Windows Gezgini'ne eklenen menüler. Kabuk (uygulamanın exe'si) uygular; yönetici izni gerekmez.</summary>
public interface IShellIntegration
{
    ShellIntegrationState Get();

    /// <summary>İstenen duruma getirir ve gerçek durumu döndürür.</summary>
    ShellIntegrationState Set(bool folderMenu, bool sendTo);
}
