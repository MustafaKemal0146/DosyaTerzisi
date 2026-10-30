namespace DosyaTerzisi.Core;

/// <summary>Converts a file name to the DOS 8.3 shape: name up to 8, extension up to 3 characters.</summary>
public static class ShortName83
{
    private const int MaxName = 8;
    private const int MaxExtension = 3;
    private const string Replaced = " .,;=+[]~\\/:*?\"<>|";

    /// <param name="name">The file name only (no folder).</param>
    /// <param name="isDirectory">Folders have no extension: the whole name gets at most 8 characters.</param>
    /// <param name="used">
    /// Names already handed out in the same folder (case-insensitive). When the name has to be cut, the first free
    /// <c>~1</c>, <c>~2</c> ... is taken. The result is added to the set.
    /// </param>
    public static string Convert(string name, bool isDirectory, ISet<string>? used = null)
    {
        string stem = name;
        string extension = "";
        if (!isDirectory)
        {
            int dot = name.LastIndexOf('.');
            if (dot >= 0)
            {
                stem = name[..dot];
                extension = name[(dot + 1)..];
            }
        }

        stem = Clean(stem);
        extension = Clean(extension);
        if (extension.Length > MaxExtension) extension = extension[..MaxExtension];
        if (stem.Length == 0) stem = "DOSYA";

        string suffix = extension.Length == 0 ? "" : "." + extension;
        string result = stem + suffix;

        if (stem.Length > MaxName)
        {
            string head = stem[..(MaxName - 2)];
            for (int n = 1; ; n++)
            {
                string tail = "~" + n;
                string cut = head[..Math.Min(head.Length, MaxName - tail.Length)];
                result = cut + tail + suffix;
                if (used is null || !used.Contains(result)) break;
            }
        }

        used?.Add(result);
        return result;
    }

    private static string Clean(string text)
    {
        var chars = text.Select(c => Replaced.Contains(c) || char.IsControl(c) ? '_' : c).ToArray();
        return new string(chars);
    }
}

/// <summary>Remembers the short names given out per folder, so the <c>~N</c> counter is unique across files.</summary>
internal sealed class ShortNameRegistry
{
    private readonly Dictionary<string, HashSet<string>> _perFolder = new(StringComparer.OrdinalIgnoreCase);

    public string Convert(string name, bool isDirectory, string directory)
    {
        string key = directory.TrimEnd('\\', '/');
        if (!_perFolder.TryGetValue(key, out var set))
            _perFolder[key] = set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return ShortName83.Convert(name, isDirectory, set);
    }
}
