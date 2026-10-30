namespace DosyaTerzisi.Core;

internal enum MoveState
{
    /// <summary>Still under its old name.</summary>
    NotDone,

    /// <summary>Under its new name.</summary>
    Done,

    /// <summary>Waiting under a temporary name (a swap was interrupted).</summary>
    Parked,

    /// <summary>Not where it should be, or both names exist.</summary>
    Unknown,
}

internal sealed record ProbeResult(MoveState State, string Directory, string TempName);

/// <summary>
/// What was already read from the disk during one pass over many moves. Without it every move would list its folder
/// again, which is quadratic: 20,000 files in one folder took minutes.
/// </summary>
internal sealed class ProbeCache
{
    public Dictionary<string, string?> Directories { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, HashSet<string>> Names { get; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>Looks at the disk to find out how far a recorded move got.</summary>
internal static class MoveProbe
{
    public static ProbeResult Probe(RenameMove move, IReadOnlyList<RenameMove> all, string tag, ProbeCache? cache = null)
    {
        string temp = RenameExecutor.TempName(tag, move.OldName);

        string? directory;
        if (cache is null) directory = ResolveDirectory(move.Directory, all, tag);
        else if (!cache.Directories.TryGetValue(move.Directory, out directory))
            cache.Directories[move.Directory] = directory = ResolveDirectory(move.Directory, all, tag);
        if (directory is null) return new ProbeResult(MoveState.Unknown, move.Directory, temp);

        // Exact comparison: a case-only rename (alpha → Alpha) must not look like "both exist".
        HashSet<string> names;
        if (cache is null) names = ListNames(directory);
        else if (!cache.Names.TryGetValue(directory, out names!))
            cache.Names[directory] = names = ListNames(directory);
        bool hasOld = names.Contains(move.OldName);
        bool hasNew = names.Contains(move.NewName);
        bool hasTemp = names.Contains(temp);

        var state = (hasOld, hasNew, hasTemp) switch
        {
            (true, false, false) => MoveState.NotDone,
            (false, true, false) => MoveState.Done,
            (false, false, true) => MoveState.Parked,
            _ => MoveState.Unknown,
        };
        return new ProbeResult(state, directory, temp);
    }

    /// <summary>Is the move still in effect: new name there, old name gone?</summary>
    public static bool IsApplied(RenameMove move, IReadOnlyList<RenameMove> all, ProbeCache? cache = null) =>
        move.IsInPlace ? Probe(move, all, "none", cache).State == MoveState.Done : TransferApplied(move);

    /// <summary>Moves into other folders, copies and links: the result is there (and, for a move, the original is gone).</summary>
    public static bool TransferApplied(RenameMove move)
    {
        string target = Path.Combine(move.TargetDirectory ?? move.Directory, move.NewName);
        bool there = File.Exists(target) || Directory.Exists(target);
        if (move.Kind != TransferKind.Rename) return there;

        string original = Path.Combine(move.Directory, move.OldName);
        return there && !File.Exists(original) && !Directory.Exists(original);
    }

    /// <summary>
    /// A move records the folder as it was before the operation. If an ancestor folder was renamed in the same
    /// operation, the folder is now somewhere else; follow the renames to find it.
    /// </summary>
    public static string? ResolveDirectory(string directory, IReadOnlyList<RenameMove> all, string tag)
    {
        string current = directory;

        for (int guard = 0; guard < 64; guard++)
        {
            if (Directory.Exists(current)) return current;

            var renamedAncestor = all
                .Where(m => m.IsDirectory && IsUnder(current, Path.Combine(m.Directory, m.OldName)))
                .OrderByDescending(m => Path.Combine(m.Directory, m.OldName).Length)
                .FirstOrDefault();
            if (renamedAncestor is null) return null;

            string oldPath = Path.Combine(renamedAncestor.Directory, renamedAncestor.OldName);
            string rest = current[oldPath.Length..];
            string viaNew = Path.Combine(renamedAncestor.Directory, renamedAncestor.NewName) + rest;
            string viaTemp = Path.Combine(renamedAncestor.Directory, RenameExecutor.TempName(tag, renamedAncestor.OldName)) + rest;

            current = Directory.Exists(viaTemp) && !Directory.Exists(viaNew) ? viaTemp : viaNew;
        }

        return null;
    }

    private static bool IsUnder(string path, string ancestor) =>
        path.Equals(ancestor, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(ancestor.TrimEnd('\\', '/') + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static HashSet<string> ListNames(string directory) =>
        new(Directory.EnumerateFileSystemEntries(directory).Select(Path.GetFileName).OfType<string>(), StringComparer.Ordinal);
}
