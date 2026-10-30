namespace DosyaTerzisi.Core;

/// <summary>What was found after a run that never finished.</summary>
public sealed record RecoveryReport(
    HistoryEntry Entry,
    int Planned,
    int Done,
    int Restored,
    IReadOnlyList<string> Problems);

/// <summary>Renames with a safety net: every operation is written to the history before it starts.</summary>
public sealed class RenameService(HistoryStore store)
{
    public HistoryStore History => store;

    public bool CanUndo => store.LatestCompleted is not null;

    /// <summary>Renames the items that are ready and records them for undo.</summary>
    /// <exception cref="IOException">The history file could not be written; nothing was renamed.</exception>
    public ExecutionResult Execute(IReadOnlyList<RenameItem> items, string description)
    {
        var ready = items.Where(i => i.Status == RenameStatus.Ready).ToList();
        if (ready.Count == 0) return new ExecutionResult(new RenameJournal([]), []);

        var planned = ready.Select(i => new RenameMove(
            i.Entry.Directory, i.Entry.Name, i.NewName, i.Entry.IsDirectory, i.DestinationDirectory, i.Kind));
        var entry = store.Begin(description, planned);

        var result = RenameExecutor.Execute(ready, entry.Id.ToString("N"));

        store.Complete(entry.Id, result.Journal.Moves);
        return result;
    }

    /// <summary>Takes back the latest operation. Moves that could not be undone stay in the history.</summary>
    public (HistoryEntry Entry, ExecutionResult Result)? UndoLatest()
    {
        if (store.LatestCompleted is not { } entry) return null;

        var result = RenameExecutor.Undo(new RenameJournal(entry.Moves));

        var cache = new ProbeCache();
        var stillApplied = entry.Moves.Where(m => MoveProbe.IsApplied(m, entry.Moves, cache)).ToList();
        store.SetMoves(entry.Id, stillApplied);
        return (entry, result);
    }

    /// <summary>
    /// Settles operations that were still marked in progress at start-up: finds out which renames happened,
    /// puts files that were waiting under a temporary name back, and turns the rest into a normal undoable entry.
    /// </summary>
    private static bool RemoveLeftover(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
            else if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
            else return false;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public IReadOnlyList<RecoveryReport> RecoverInterrupted()
    {
        var reports = new List<RecoveryReport>();

        foreach (var entry in store.Interrupted)
        {
            string tag = entry.Id.ToString("N");
            var done = new List<RenameMove>();
            var problems = new List<string>();
            int restored = 0;

            // Moves into other folders, copies and links have no temporary parking: they either happened or they did not.
            foreach (var transfer in entry.Moves.Where(m => !m.IsInPlace))
            {
                if (MoveProbe.TransferApplied(transfer))
                {
                    done.Add(transfer);
                    continue;
                }

                // A copy that was cut off leaves a file under its temporary name; it is ours, so it can go.
                string temp = Path.Combine(transfer.TargetDirectory ?? transfer.Directory, RenameExecutor.TempName(tag, transfer.NewName));
                if (RemoveLeftover(temp)) restored++;
            }

            // Look at every move before touching anything: restoring one file changes what the others look like.
            var cache = new ProbeCache();
            var probes = entry.Moves.Where(m => m.IsInPlace).Select(m => (Move: m, Probe: MoveProbe.Probe(m, entry.Moves, tag, cache))).ToList();

            foreach (var (move, probe) in probes)
            {
                switch (probe.State)
                {
                    case MoveState.Done:
                        done.Add(move);
                        break;

                    case MoveState.Parked:
                        if (RenameExecutor.TryMove(probe.Directory, probe.TempName, move.OldName, move.IsDirectory, out var error))
                            restored++;
                        else
                            problems.Add($"{Path.Combine(probe.Directory, probe.TempName)}: {error}");
                        break;

                    case MoveState.Unknown:
                        problems.Add($"{Path.Combine(move.Directory, move.OldName)} → {move.NewName}: durumu anlaşılamadı");
                        break;
                }
            }

            store.Complete(entry.Id, done);
            reports.Add(new RecoveryReport(entry, entry.Moves.Count, done.Count, restored, problems));
        }

        return reports;
    }
}
