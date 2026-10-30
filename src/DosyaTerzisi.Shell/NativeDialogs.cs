using System.Windows;
using DosyaTerzisi.Bridge;
using Microsoft.Win32;

namespace DosyaTerzisi.Shell;

/// <summary>Windows'un kendi klasör ve dosya pencereleri. Web arayüzü bunları köprü üzerinden ister.</summary>
public sealed class NativeDialogs(Window owner) : INativeDialogs
{
    public Task<string?> PickFolderAsync(string? initialFolder)
    {
        var dialog = new OpenFolderDialog { Title = "Klasör seçin" };
        if (initialFolder is not null) dialog.InitialDirectory = initialFolder;

        return Task.FromResult<string?>(dialog.ShowDialog(owner) == true ? dialog.FolderName : null);
    }

    public Task<string?> PickOpenFileAsync(string title, string filter)
    {
        var dialog = new OpenFileDialog { Title = title, Filter = filter };
        return Task.FromResult<string?>(dialog.ShowDialog(owner) == true ? dialog.FileName : null);
    }

    public Task OpenInEditorAsync(string path)
    {
        // Önce dosyanın ilişkili programı (.vbs için genellikle Windows Betik Ana Bilgisayarı, yani çalıştırır!):
        // bu yüzden doğrudan Not Defteri açılır.
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("notepad.exe")
        {
            UseShellExecute = true,
            ArgumentList = { path },
        });
        return Task.CompletedTask;
    }

    public Task<string?> PickSaveFileAsync(string title, string filter, string defaultName)
    {
        var dialog = new SaveFileDialog { Title = title, Filter = filter, FileName = defaultName };
        return Task.FromResult<string?>(dialog.ShowDialog(owner) == true ? dialog.FileName : null);
    }
}
