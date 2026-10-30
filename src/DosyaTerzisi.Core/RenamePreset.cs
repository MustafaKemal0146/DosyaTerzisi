using System.Text;

namespace DosyaTerzisi.Core;

/// <summary>One line of the preset list (<c>OnAyarlar.txt</c>): <c>Preset=Mode, Script, Search, Replace, Name</c> (tab separated).</summary>
public sealed record RenamePreset(RenameMethod Method, string Script, string Search, string Replace, string Name)
{
    public RenameOptions ToOptions() => new()
    {
        Method = Method,
        Search = Search,
        Replace = Replace,
    };
}

public static class PresetFile
{
    public static IReadOnlyList<RenamePreset> Load(string path)
    {
        // UTF-8 (with or without BOM) and UTF-16 are accepted.
        using var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return Parse(reader.ReadToEnd());
    }

    public static IReadOnlyList<RenamePreset> Parse(string text)
    {
        var presets = new List<RenamePreset>();

        foreach (var line in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            const string key = "Preset=";
            if (!line.StartsWith(key, StringComparison.Ordinal)) continue;

            var fields = line[key.Length..].Split('\t');
            if (fields.Length < 5) continue;
            if (!int.TryParse(fields[0], out int mode) || !Enum.IsDefined((RenameMethod)mode)) continue;

            presets.Add(new RenamePreset((RenameMethod)mode, fields[1], fields[2], fields[3], fields[4]));
        }

        return presets;
    }
}
