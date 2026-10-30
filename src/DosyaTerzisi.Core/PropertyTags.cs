using System.Runtime.Versioning;

namespace DosyaTerzisi.Core;

/// <summary>
/// The properties that the Windows shell shows for a file (the columns of Explorer's details view) as <c>PROP.&lt;column&gt;</c>
/// tags, with the column names of the computer's language, such as <c>PROP.Boyutlar</c>. Spaces and special characters
/// are removed from the column name. Only used when the tags of one file are asked for.
/// </summary>
internal static class PropertyTags
{
    private const int ColumnCount = 321;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private static readonly object Gate = new();
    private static string[]? _columns;

    public static void Read(string path, Dictionary<string, TagValue> values)
    {
        if (!OperatingSystem.IsWindows()) return;

        try
        {
            List<(string Name, string Text)>? found = null;
            var thread = new Thread(() =>
            {
                try { if (OperatingSystem.IsWindows()) found = Query(path); }
                catch (Exception)
                {
                    // The shell is unavailable or does not know the file: no PROP tags.
                }
            })
            {
                IsBackground = true,
            };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            if (!thread.Join(Timeout) || found is null) return;

            foreach (var (name, text) in found) values.TryAdd("PROP." + name, new TagValue(text));
        }
        catch (Exception)
        {
            // Never let the shell break the other tags.
        }
    }

    /// <summary>Turns a column name such as "Bit hızı" into "Bithızı": only letters, digits and hyphens stay.</summary>
    internal static string CleanName(string? name) =>
        name is null ? "" : new string(name.Where(c => char.IsLetterOrDigit(c) || c == '-').ToArray());

    private static object? Call(object target, string name, params object?[] args) =>
        target.GetType().InvokeMember(name, System.Reflection.BindingFlags.InvokeMethod, null, target, args);

    [SupportedOSPlatform("windows")]
    private static List<(string Name, string Text)> Query(string path)
    {
        var result = new List<(string, string)>();
        var type = Type.GetTypeFromProgID("Shell.Application");
        if (type is null) return result;

        // One query at a time: the shell objects are cheap but not meant to be driven from many threads at once.
        lock (Gate)
        {
            try
            {
                object? shell = Activator.CreateInstance(type);
                string? directory = Path.GetDirectoryName(path);
                if (shell is null || string.IsNullOrEmpty(directory)) return result;

                object? folder = Call(shell, "NameSpace", directory);
                if (folder is null) return result;
                object? item = Call(folder, "ParseName", Path.GetFileName(path));
                if (item is null) return result;

                if (_columns is null)
                {
                    var names = new string[ColumnCount];
                    for (int i = 0; i < ColumnCount; i++) names[i] = CleanName(Call(folder, "GetDetailsOf", null, i) as string);
                    _columns = names;
                }

                for (int i = 1; i < _columns.Length; i++)
                {
                    if (_columns[i].Length == 0) continue;
                    if (Call(folder, "GetDetailsOf", item, i) is not string text) continue;

                    text = text.Replace("‎", "").Replace("‏", "").Trim();
                    if (text.Length > 0) result.Add((_columns[i], text));
                }
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                // This file has no shell item; keep what was read so far.
            }
        }

        return result;
    }
}
