using System.Text.Json;
using System.Text.Json.Serialization;

namespace DosyaTerzisi.Core;

public enum OperationState
{
    /// <summary>Written before the first file is touched. Still present at the next start = the program stopped halfway.</summary>
    InProgress,

    Completed,
}

public sealed record HistoryEntry(
    Guid Id,
    DateTime Time,
    string Description,
    OperationState State,
    IReadOnlyList<RenameMove> Moves);

/// <summary>
/// The list of rename operations, kept in a JSON file so they can still be undone after the program was closed,
/// crashed or the computer lost power. The file is replaced atomically on every change.
/// </summary>
public sealed class HistoryStore
{
    public const int MaxEntries = 100;

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly object _gate = new();
    private readonly string _path;
    private List<HistoryEntry> _entries = [];

    public HistoryStore(string path)
    {
        _path = path;
        Load();
    }

    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DosyaTerzisi", "history.json");

    /// <summary>Set when the file could not be read; the unreadable file is kept next to it with a .bad suffix.</summary>
    public string? LoadWarning { get; private set; }

    public IReadOnlyList<HistoryEntry> Entries
    {
        get
        {
            lock (_gate) return [.. _entries];
        }
    }

    /// <summary>The operation Undo will take back next.</summary>
    public HistoryEntry? LatestCompleted => Entries.LastOrDefault(e => e.State == OperationState.Completed);

    public IReadOnlyList<HistoryEntry> Interrupted => Entries.Where(e => e.State == OperationState.InProgress).ToList();

    /// <summary>Records what is about to happen. Throws when the file cannot be written, so nothing is renamed unrecorded.</summary>
    public HistoryEntry Begin(string description, IEnumerable<RenameMove> planned)
    {
        var entry = new HistoryEntry(Guid.NewGuid(), DateTime.Now, description, OperationState.InProgress, planned.ToList());
        lock (_gate)
        {
            _entries.Add(entry);
            Trim();
            Save();
        }
        return entry;
    }

    /// <summary>Marks the operation finished with the moves that really happened. Nothing done = entry is dropped.</summary>
    public void Complete(Guid id, IReadOnlyList<RenameMove> done) => Replace(id, done);

    /// <summary>Keeps only the moves that are still in effect, for example after a partly failed undo.</summary>
    public void SetMoves(Guid id, IReadOnlyList<RenameMove> remaining) => Replace(id, remaining);

    public void Remove(Guid id) => Replace(id, []);

    private void Replace(Guid id, IReadOnlyList<RenameMove> moves)
    {
        lock (_gate)
        {
            int index = _entries.FindIndex(e => e.Id == id);
            if (index < 0) return;

            if (moves.Count == 0) _entries.RemoveAt(index);
            else _entries[index] = _entries[index] with { State = OperationState.Completed, Moves = moves };

            Save();
        }
    }

    private void Trim()
    {
        while (_entries.Count > MaxEntries)
        {
            int oldest = _entries.FindIndex(e => e.State == OperationState.Completed);
            if (oldest < 0) break;
            _entries.RemoveAt(oldest);
        }
    }

    private void Load()
    {
        if (!File.Exists(_path)) return;

        try
        {
            _entries = JsonSerializer.Deserialize<List<HistoryEntry>>(File.ReadAllText(_path), Json) ?? [];
            _entries.RemoveAll(e => e is null || e.Moves is null);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            string backup = $"{_path}.bad-{DateTime.Now:yyyyMMdd-HHmmss}";
            try
            {
                File.Move(_path, backup, overwrite: true);
                LoadWarning = $"Geri alma geçmişi okunamadı, yedeği saklandı: {backup}";
            }
            catch (IOException)
            {
                LoadWarning = "Geri alma geçmişi okunamadı.";
            }
            _entries = [];
        }
        catch (IOException ex)
        {
            LoadWarning = $"Geri alma geçmişi okunamadı: {ex.Message}";
            _entries = [];
        }
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);

        string temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(_entries, Json));
        File.Move(temp, _path, overwrite: true);
    }
}
