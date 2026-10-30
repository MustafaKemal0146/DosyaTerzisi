using DosyaTerzisi.Bridge;
using DosyaTerzisi.Bridge.Contract;
using Microsoft.Win32;

namespace DosyaTerzisi.Tests;

/// <summary>Gezgin menüleri: gerçek kayıt defteri ve gerçek kısayol, ama bu denemeye özel bir anahtarda ve klasörde.</summary>
public sealed class ShellIntegrationTests : IDisposable
{
    private readonly string _root = $@"Software\DosyaTerzisiTest-{Guid.NewGuid():N}";
    private readonly TempFolder _sendTo = new();
    private const string Exe = @"C:\Program Files\Dosya Terzisi\DosyaTerzisi.exe";

    private ShellIntegration Create() => new(Exe, _sendTo.Root, _root);

    public void Dispose()
    {
        Registry.CurrentUser.DeleteSubKeyTree(_root, throwOnMissingSubKey: false);
        _sendTo.Dispose();
    }

    [Fact]
    public void NothingIsInstalledAtFirst()
    {
        var state = Create().Get();

        Assert.True(state.Available);
        Assert.False(state.FolderMenu);
        Assert.False(state.SendTo);
    }

    [Fact]
    public void FolderMenu_WritesTheCommandForBothPlaces_AndCanBeRemoved()
    {
        var integration = Create();

        var on = integration.Set(folderMenu: true, sendTo: false);

        Assert.True(on.FolderMenu);
        using (var onFolder = Registry.CurrentUser.OpenSubKey($@"{_root}\Directory\shell\DosyaTerzisi\command"))
            Assert.Equal($"\"{Exe}\" \"%1\"", onFolder!.GetValue(""));
        using (var inside = Registry.CurrentUser.OpenSubKey($@"{_root}\Directory\Background\shell\DosyaTerzisi\command"))
            Assert.Equal($"\"{Exe}\" \"%V\"", inside!.GetValue(""));

        var off = integration.Set(folderMenu: false, sendTo: false);

        Assert.False(off.FolderMenu);
        Assert.Null(Registry.CurrentUser.OpenSubKey($@"{_root}\Directory\shell\DosyaTerzisi"));
        Assert.Null(Registry.CurrentUser.OpenSubKey($@"{_root}\Directory\Background\shell\DosyaTerzisi"));
    }

    [Fact]
    public void SendTo_CreatesARealShortcut_AndRemovesIt()
    {
        var integration = Create();

        var on = integration.Set(folderMenu: false, sendTo: true);

        Assert.True(on.SendTo);
        Assert.True(File.Exists(Path.Combine(_sendTo.Root, "Dosya Terzisi.lnk")));

        var off = integration.Set(folderMenu: false, sendTo: false);

        Assert.False(off.SendTo);
        Assert.Empty(Directory.GetFiles(_sendTo.Root));
    }

    [Fact]
    public void SettingTwice_IsHarmless()
    {
        var integration = Create();
        integration.Set(true, true);

        var again = integration.Set(true, true);

        Assert.True(again.FolderMenu);
        Assert.True(again.SendTo);
    }

    [Fact]
    public void AMenuForAnotherProgramPath_DoesNotCountAsInstalled()
    {
        Create().Set(true, false);
        var moved = new ShellIntegration(@"D:\Başka\DosyaTerzisi.exe", _sendTo.Root, _root);

        Assert.False(moved.Get().FolderMenu);
    }

    [Fact]
    public async Task ThroughTheBridge_StateCanBeReadAndChanged()
    {
        using var h = new BridgeHarness(integration: Create());

        var before = await h.Call<ShellIntegrationState>("shellIntegration", new Empty());
        var after = await h.Call<ShellIntegrationState>("setShellIntegration", new SetShellIntegrationParams(true, true));

        Assert.False(before.FolderMenu);
        Assert.True(after.FolderMenu && after.SendTo);
    }

    [Fact]
    public async Task WithoutAnIntegration_ItIsReportedAsUnavailable()
    {
        using var h = new BridgeHarness();

        var state = await h.Call<ShellIntegrationState>("shellIntegration", new Empty());

        Assert.False(state.Available);
        await Assert.ThrowsAsync<BridgeError>(() => h.Call<ShellIntegrationState>("setShellIntegration", new SetShellIntegrationParams(true, true)));
    }
}

public class CommandLineTests
{
    [Fact]
    public async Task Init_ReportsTheFilesGivenOnTheCommandLine()
    {
        using var h = new BridgeHarness(initialPaths: [@"C:\a.txt", @"C:\b.txt"]);

        var init = await h.Call<InitState>("init", new Empty());

        Assert.Equal([@"C:\a.txt", @"C:\b.txt"], init.Paths);
        Assert.Equal("", init.Folder);
    }
}
