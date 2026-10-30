using System.Security.Cryptography;
using System.Text;

namespace DosyaTerzisi.Core;

/// <summary>One finished rename. <see cref="Directory"/> is the folder as it was before the operation started.</summary>
/// <param name="TargetDirectory">Where the new name lives, when that is another folder (move, copy, link). Null = same folder.</param>
/// <param name="Kind">What was done under the new name.</param>
/// <param name="CreatedDirectories">Folders this move had to create, outermost first; removed again on undo when empty.</param>
public sealed record RenameMove(
    string Directory,
    string OldName,
    string NewName,
    bool IsDirectory,
    string? TargetDirectory = null,
    TransferKind Kind = TransferKind.Rename,
    IReadOnlyList<string>? CreatedDirectories = null)
{
    /// <summary>A plain rename inside one folder: the only kind that can need temporary names.</summary>
    public bool IsInPlace => Kind == TransferKind.Rename && TargetDirectory is null;
}

/// <summary>Everything one rename operation did, so it can be undone.</summary>
public sealed class RenameJournal(IReadOnlyList<RenameMove> moves)
{
    public IReadOnlyList<RenameMove> Moves { get; } = moves;
}

public sealed record RenameFailure(string Path, string NewName, string Message);

public sealed record ExecutionResult(RenameJournal Journal, IReadOnlyList<RenameFailure> Failures);

/// <summary>Performs the renames of a plan. Never overwrites an existing file or folder.</summary>
public static class RenameExecutor
{
    /// <param name="tag">
    /// Marks the temporary names used for swaps (<see cref="TempName"/>), so an interrupted run can be repaired later.
    /// </param>
    public static ExecutionResult Execute(IEnumerable<RenameItem> items, string? tag = null)
    {
        tag ??= NewTag();
        var ready = items.Where(i => i.Status == RenameStatus.Ready).ToList();
        var inPlace = ready.Where(i => i.Kind == TransferKind.Rename && i.DestinationDirectory is null).ToList();

        // Moves, copies and links first, while every source still has its original path.
        var transfers = TransferExecutor.Execute(ready.Except(inPlace), tag);

        var moves = inPlace.Select(i => new RenameMove(i.Entry.Directory, i.Entry.Name, i.NewName, i.Entry.IsDirectory));

        // Contents of a folder go first: renaming the folder afterwards cannot break their paths.
        var renamed = Run(moves, deepestFirst: true, tag);

        return new ExecutionResult(
            new RenameJournal([.. transfers.Journal.Moves, .. renamed.Journal.Moves]),
            [.. transfers.Failures, .. renamed.Failures]);
    }

    public static ExecutionResult Undo(RenameJournal journal)
    {
        // Reverse of Execute: plain renames first (the originals get their paths back), then the transfers.
        var inPlace = journal.Moves.Where(m => m.IsInPlace).Select(m => m with { OldName = m.NewName, NewName = m.OldName });

        // Folders are restored first, so the paths recorded for their contents are valid again.
        var renamed = Run(inPlace, deepestFirst: false, NewTag());
        var transfers = TransferExecutor.Undo(journal.Moves.Where(m => !m.IsInPlace));

        return new ExecutionResult(
            new RenameJournal([.. transfers.Journal.Moves, .. renamed.Journal.Moves]),
            [.. renamed.Failures, .. transfers.Failures]);
    }

    /// <summary>
    /// Name a file gets while it waits for its final name. Derived from the tag and the old name only, so it can be
    /// recomputed after a crash to find the file again.
    /// </summary>
    internal static string TempName(string tag, string oldName)
    {
        var hash = SHA1.HashData(Encoding.UTF8.GetBytes(oldName.ToLowerInvariant()));
        return $".terzi-{tag}-{Convert.ToHexString(hash, 0, 6)}";
    }

    private static string NewTag() => Guid.NewGuid().ToString("N");

    private static ExecutionResult Run(IEnumerable<RenameMove> moves, bool deepestFirst, string tag)
    {
        var done = new List<RenameMove>();
        var failures = new List<RenameFailure>();

        var groups = moves
            .GroupBy(m => m.Directory, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => deepestFirst ? -Depth(g.Key) : Depth(g.Key));

        foreach (var group in groups)
        {
            var list = group.ToList();
            if (NeedsTemporaryNames(list))
                RunWithTemporaryNames(list, tag, done, failures);
            else
                RunDirect(list, done, failures);
        }

        return new ExecutionResult(new RenameJournal(done), failures);
    }

    /// <summary>Swaps, chains (a→b, b→c) and case-only changes of folders cannot be done one by one.</summary>
    private static bool NeedsTemporaryNames(List<RenameMove> moves)
    {
        var oldNames = new HashSet<string>(moves.Select(m => m.OldName), StringComparer.OrdinalIgnoreCase);
        return moves.Any(m => oldNames.Contains(m.NewName));
    }

    private static void RunDirect(List<RenameMove> moves, List<RenameMove> done, List<RenameFailure> failures)
    {
        foreach (var move in moves)
        {
            if (TryMove(move.Directory, move.OldName, move.NewName, move.IsDirectory, out var error))
                done.Add(move);
            else
                failures.Add(new RenameFailure(Path.Combine(move.Directory, move.OldName), move.NewName, error));
        }
    }

    private static void RunWithTemporaryNames(List<RenameMove> moves, string tag, List<RenameMove> done, List<RenameFailure> failures)
    {
        var parked = new List<(RenameMove Move, string Temp)>();

        foreach (var move in moves)
        {
            string temp = TempName(tag, move.OldName);
            if (TryMove(move.Directory, move.OldName, temp, move.IsDirectory, out var error))
                parked.Add((move, temp));
            else
                failures.Add(new RenameFailure(Path.Combine(move.Directory, move.OldName), move.NewName, error));
        }

        foreach (var (move, temp) in parked)
        {
            if (TryMove(move.Directory, temp, move.NewName, move.IsDirectory, out var error))
            {
                done.Add(move);
                continue;
            }

            // Put it back under its old name; if even that fails the temporary name is reported.
            bool restored = TryMove(move.Directory, temp, move.OldName, move.IsDirectory, out _);
            string where = restored ? Path.Combine(move.Directory, move.OldName) : Path.Combine(move.Directory, temp);
            failures.Add(new RenameFailure(where, move.NewName, error));
        }
    }

    internal static bool TryMove(string directory, string from, string to, bool isDirectory, out string error)
    {
        string source = Path.Combine(directory, from);
        string target = Path.Combine(directory, to);
        try
        {
            if (isDirectory) Directory.Move(source, target);
            else File.Move(source, target);
            error = "";
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = ex.Message;
            return false;
        }
    }

    private static int Depth(string path) => path.Count(c => c is '\\' or '/');
}
