using System.Runtime.Versioning;
using DosyaTerzisi.Bridge.Contract;
using Microsoft.Win32;

namespace DosyaTerzisi.Bridge;

/// <summary>
/// Gezgin menüleri, yönetici izni olmadan (yalnızca bu kullanıcı için):
/// klasöre sağ tık → "Dosya Terzisi ile yeniden adlandır" (kayıt defteri, HKCU) ve
/// dosyalara sağ tık → Gönder → Dosya Terzisi (SendTo klasöründe kısayol).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ShellIntegration(string exePath, string? sendToFolder = null, string classesRoot = @"Software\Classes") : IShellIntegration
{
    private const string MenuName = "DosyaTerzisi";
    private const string MenuText = "Dosya Terzisi ile yeniden adlandır";
    private const string ShortcutName = "Dosya Terzisi.lnk";

    private readonly string _sendTo = sendToFolder ?? Environment.GetFolderPath(Environment.SpecialFolder.SendTo);

    // Klasörün üzerine sağ tık (%1) ve klasörün içinde boş yere sağ tık (%V).
    private static readonly (string Key, string Argument)[] Targets =
    [
        (@"Directory\shell", "%1"),
        (@"Directory\Background\shell", "%V"),
    ];

    public ShellIntegrationState Get() => new(true, FolderMenuInstalled(), File.Exists(ShortcutPath));

    public ShellIntegrationState Set(bool folderMenu, bool sendTo)
    {
        if (folderMenu) InstallFolderMenu();
        else RemoveFolderMenu();

        if (sendTo) InstallSendTo();
        else if (File.Exists(ShortcutPath)) File.Delete(ShortcutPath);

        return Get();
    }

    private string ShortcutPath => Path.Combine(_sendTo, ShortcutName);

    private string CommandFor(string argument) => $"\"{exePath}\" \"{argument}\"";

    private bool FolderMenuInstalled()
    {
        using var command = Registry.CurrentUser.OpenSubKey($@"{classesRoot}\{Targets[0].Key}\{MenuName}\command");
        return command?.GetValue("") is string value && value.Equals(CommandFor(Targets[0].Argument), StringComparison.OrdinalIgnoreCase);
    }

    private void InstallFolderMenu()
    {
        foreach (var (key, argument) in Targets)
        {
            using var menu = Registry.CurrentUser.CreateSubKey($@"{classesRoot}\{key}\{MenuName}");
            menu.SetValue("", MenuText);
            menu.SetValue("Icon", $"\"{exePath}\",0");
            using var command = menu.CreateSubKey("command");
            command.SetValue("", CommandFor(argument));
        }
    }

    private void RemoveFolderMenu()
    {
        foreach (var (key, _) in Targets)
            Registry.CurrentUser.DeleteSubKeyTree($@"{classesRoot}\{key}\{MenuName}", throwOnMissingSubKey: false);
    }

    private void InstallSendTo()
    {
        Directory.CreateDirectory(_sendTo);
        if (File.Exists(ShortcutPath)) File.Delete(ShortcutPath);

        var shellType = Type.GetTypeFromProgID("WScript.Shell") ?? throw new IOException("Kısayol oluşturma bu bilgisayarda kullanılamıyor.");
        dynamic shell = Activator.CreateInstance(shellType)!;
        try
        {
            dynamic link = shell.CreateShortcut(ShortcutPath);
            link.TargetPath = exePath;
            link.WorkingDirectory = Path.GetDirectoryName(exePath) ?? "";
            link.Description = "Dosya Terzisi";
            link.Save();
        }
        finally
        {
            System.Runtime.InteropServices.Marshal.ReleaseComObject(shell);
        }
    }
}
