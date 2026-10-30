using DosyaTerzisi.Core;

namespace DosyaTerzisi.Tests;

internal static class TestHelpers
{
    public static readonly DateTime Stamp = new(2026, 7, 1, 15, 33, 7);

    public static FileEntry File(string name, string directory = @"C:\Belgeler\Evrak", DateTime? modified = null) =>
        new(directory, name, false, modified ?? Stamp, Stamp);

    public static FileEntry Folder(string name, string directory = @"C:\Belgeler\Evrak") =>
        new(directory, name, true, Stamp, Stamp);

    public static RenameOptions Options(RenameMethod method, string search, string replace) =>
        new() { Method = method, Search = search, Replace = replace };

    /// <summary>Applies a single rule to a single entry through the planner and returns the new name.</summary>
    public static string Rename(FileEntry entry, RenameOptions options) =>
        RenamePlanner.Build([entry], options, NoDisk)[0].NewName;

    /// <summary>Plan options that never read the real disk.</summary>
    public static PlanOptions NoDisk => new() { ListDirectory = _ => [] };

    public static string PresetPath => Path.Combine(AppContext.BaseDirectory, "TestData", "OnAyarlar.txt");
}
