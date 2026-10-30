using System.Text.Json;
using System.Text.Json.Serialization;

namespace DosyaTerzisi.Core;

/// <summary>A search/replace rule the person saved under a name.</summary>
public sealed record Favorite(
    Guid Id,
    string Name,
    RenameMethod Method,
    string Search,
    string Replace,
    bool CaseSensitive = false)
{
    public RenameOptions ToOptions() => new()
    {
        Method = Method,
        Search = Search,
        Replace = Replace,
        CaseSensitive = CaseSensitive,
    };

    /// <summary>Same rule, whatever the name or id.</summary>
    public bool SameRuleAs(Favorite other) =>
        Method == other.Method && Search == other.Search && Replace == other.Replace && CaseSensitive == other.CaseSensitive;
}

/// <summary>
/// The person's own presets. Kept in a JSON file (replaced atomically on every change); with no path they only live in memory.
/// Names are unique: a second "Rapor" becomes "Rapor (2)".
/// </summary>
public sealed class FavoriteStore
{
    private sealed record FavoriteFile(int Version, List<Favorite> Favorites);

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly object _gate = new();
    private readonly string? _path;
    private List<Favorite> _items = [];

    public FavoriteStore(string? path = null)
    {
        _path = path;
        if (path is not null) Load(path);
    }

    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DosyaTerzisi", "favorites.json");

    /// <summary>Set when the file could not be read; an unreadable file is kept next to it with a .bad suffix.</summary>
    public string? LoadWarning { get; private set; }

    public event EventHandler? Changed;

    public IReadOnlyList<Favorite> Items
    {
        get
        {
            lock (_gate) return [.. _items];
        }
    }

    public Favorite Add(string name, RenameMethod method, string search, string replace, bool caseSensitive = false)
    {
        Favorite favorite;
        lock (_gate)
        {
            favorite = new Favorite(Guid.NewGuid(), UniqueName(name, null), method, search, replace, caseSensitive);
            _items.Add(favorite);
            Save();
        }
        Changed?.Invoke(this, EventArgs.Empty);
        return favorite;
    }

    /// <summary>Replaces the favorite that has the same id. Returns the stored version (the name may have been made unique).</summary>
    public Favorite? Update(Favorite favorite)
    {
        Favorite stored;
        lock (_gate)
        {
            int index = _items.FindIndex(f => f.Id == favorite.Id);
            if (index < 0) return null;

            stored = favorite with { Name = UniqueName(favorite.Name, favorite.Id) };
            _items[index] = stored;
            Save();
        }
        Changed?.Invoke(this, EventArgs.Empty);
        return stored;
    }

    public bool Remove(Guid id)
    {
        lock (_gate)
        {
            if (_items.RemoveAll(f => f.Id == id) == 0) return false;
            Save();
        }
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Moves a favorite up (negative) or down (positive) in the list.</summary>
    public bool Move(Guid id, int delta)
    {
        lock (_gate)
        {
            int from = _items.FindIndex(f => f.Id == id);
            int to = from + delta;
            if (from < 0 || to < 0 || to >= _items.Count || delta == 0) return false;

            var item = _items[from];
            _items.RemoveAt(from);
            _items.Insert(to, item);
            Save();
        }
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Writes every favorite to a file that <see cref="Import"/> reads back.</summary>
    public void Export(string path)
    {
        List<Favorite> items;
        lock (_gate) items = [.. _items];

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(new FavoriteFile(1, items), Json));
    }

    /// <summary>
    /// Adds favorites from a file made by <see cref="Export"/> or from a preset list (<c>OnAyarlar.txt</c>).
    /// Rules that are already in the list are skipped.
    /// </summary>
    /// <returns>How many favorites were added.</returns>
    /// <exception cref="InvalidDataException">The file is not in a known format.</exception>
    public int Import(string path)
    {
        var incoming = path.EndsWith(".ini", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)
            ? ReadPresetList(path)
            : ReadJson(path);

        int added = 0;
        lock (_gate)
        {
            foreach (var item in incoming)
            {
                if (_items.Any(f => f.SameRuleAs(item))) continue;

                _items.Add(item with { Id = Guid.NewGuid(), Name = UniqueName(item.Name, null) });
                added++;
            }
            if (added > 0) Save();
        }
        if (added > 0) Changed?.Invoke(this, EventArgs.Empty);
        return added;
    }

    private static List<Favorite> ReadPresetList(string path)
    {
        try
        {
            return PresetFile.Load(path)
                .Select(p => new Favorite(Guid.NewGuid(), p.Name, p.Method, p.Search, p.Replace))
                .ToList();
        }
        catch (IOException ex)
        {
            throw new InvalidDataException(ex.Message, ex);
        }
    }

    private static List<Favorite> ReadJson(string path)
    {
        try
        {
            var file = JsonSerializer.Deserialize<FavoriteFile>(File.ReadAllText(path), Json)
                       ?? throw new InvalidDataException("Dosya boş.");
            return (file.Favorites ?? []).Where(f => f is not null && !string.IsNullOrWhiteSpace(f.Name)).ToList();
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or IOException)
        {
            throw new InvalidDataException($"Favori dosyası okunamadı: {ex.Message}", ex);
        }
    }

    private string UniqueName(string name, Guid? ownId)
    {
        string baseName = string.IsNullOrWhiteSpace(name) ? "Adsız" : name.Trim();
        string candidate = baseName;
        for (int n = 2; _items.Any(f => f.Id != ownId && f.Name.Equals(candidate, StringComparison.CurrentCultureIgnoreCase)); n++)
            candidate = $"{baseName} ({n})";
        return candidate;
    }

    private void Load(string path)
    {
        if (!File.Exists(path)) return;

        try
        {
            _items = ReadJson(path);
        }
        catch (InvalidDataException)
        {
            string backup = $"{path}.bad-{DateTime.Now:yyyyMMdd-HHmmss}";
            try
            {
                File.Move(path, backup, overwrite: true);
                LoadWarning = $"Favoriler okunamadı, yedeği saklandı: {backup}";
            }
            catch (IOException)
            {
                LoadWarning = "Favoriler okunamadı.";
            }
            _items = [];
        }
    }

    private void Save()
    {
        if (_path is null) return;

        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        string temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(new FavoriteFile(1, _items), Json));
        File.Move(temp, _path, overwrite: true);
    }
}
