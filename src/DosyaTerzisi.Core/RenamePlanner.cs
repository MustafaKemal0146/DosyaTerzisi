using System.Globalization;

namespace DosyaTerzisi.Core;

public enum RenameStatus
{
    /// <summary>New name equals the old name.</summary>
    Unchanged,

    /// <summary>Can be renamed.</summary>
    Ready,

    /// <summary>Another item or an existing file already uses the new name.</summary>
    Conflict,

    /// <summary>A tag the new name needs is not in the file.</summary>
    MissingTag,

    /// <summary>The new name is not a legal Windows file name.</summary>
    InvalidName,

    /// <summary>The rule itself is broken, for example an invalid regular expression.</summary>
    Error,
}

/// <param name="NewName">The new file name only; any folder part is in <paramref name="DestinationDirectory"/>.</param>
/// <param name="DestinationDirectory">The folder the result goes into when that is not the item's own folder (or for copies and links).</param>
public sealed record RenameItem(
    FileEntry Entry,
    string NewName,
    RenameStatus Status,
    string? Message = null,
    string? DestinationDirectory = null,
    TransferKind Kind = TransferKind.Rename)
{
    /// <summary>The folder the result ends up in.</summary>
    public string TargetDirectory => DestinationDirectory ?? Entry.Directory;

    public bool WillRename => Status == RenameStatus.Ready;
}

public enum ConflictPolicy
{
    /// <summary>Mark the later item as <see cref="RenameStatus.Conflict"/>.</summary>
    Report,

    /// <summary>Insert the duplicate format (default <c>(??)</c>) before the extension until the name is free.</summary>
    AutoNumber,
}

public sealed class PlanOptions
{
    public ConflictPolicy Conflict { get; set; } = ConflictPolicy.Report;

    public string DuplicateFormat { get; set; } = "(??)";

    public int DuplicateStart { get; set; } = 1;

    /// <summary>Lists the names in a folder. Defaults to the real file system; tests replace it.</summary>
    public Func<string, IEnumerable<string>>? ListDirectory { get; set; }
}

/// <summary>Turns a list of entries and a rule into a preview of new names, without touching the disk.</summary>
public static class RenamePlanner
{
    public static IReadOnlyList<RenameItem> Build(
        IReadOnlyList<FileEntry> entries, RenameOptions options, PlanOptions? plan = null)
    {
        plan ??= new PlanOptions();

        RenameRule rule;
        try
        {
            rule = RenameRule.Create(options);
        }
        catch (RenameRuleException ex)
        {
            return entries.Select(e => new RenameItem(e, e.Name, RenameStatus.Error, ex.Message)).ToList();
        }

        var items = ApplyRule(entries, options, rule);
        ResolveConflicts(items, options, plan);
        return items;
    }

    private static List<RenameItem> ApplyRule(IReadOnlyList<FileEntry> entries, RenameOptions options, RenameRule rule)
    {
        var items = new List<RenameItem>(entries.Count);
        var numbers = new NumberSequence(options);
        var shortNames = options.ShortNames83 ? new ShortNameRegistry() : null;
        if (rule.UsesScript) return ApplyScript(entries, options, rule, shortNames);

        foreach (var entry in entries)
        {
            // With "restart in every folder" a path in the new name decides the group: files going into the same
            // new folder are counted together.
            string folder = options.ResetNumberPerFolder && rule.Apply(entry, options.NumberStart).Contains('/')
                ? DestinationResolver.Resolve(entry, rule.Apply(entry, options.NumberStart), options.Kind, options.TargetFolder).Directory
                : entry.Directory;
            string group = NumberGroups.Key(entry, options, folder);

            string newName = rule.Apply(entry, numbers.Next(entry, group));
            if (options.NameOverrides is { } typed && typed.TryGetValue(entry.FullPath, out var manual))
                newName = manual;

            items.Add(MakeItem(entry, newName, options.Kind, options.TargetFolder, shortNames));
        }

        return items;
    }

    /// <summary>
    /// Script mode: two passes. First every item's expression is built, then all of them are evaluated in one go
    /// and the names are handed out. A script error only marks its own item. (The "restart in every folder" group
    /// uses the item's own folder here: the new name is not known before the script has run.)
    /// </summary>
    private static List<RenameItem> ApplyScript(
        IReadOnlyList<FileEntry> entries, RenameOptions options, RenameRule rule, ShortNameRegistry? shortNames)
    {
        var numbers = new NumberSequence(options);
        var jobs = new ScriptJob?[entries.Count];
        var expressions = new List<string>();

        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            int number = numbers.Next(entry, NumberGroups.Key(entry, options, entry.Directory));
            if (options.NameOverrides is { } typed && typed.ContainsKey(entry.FullPath)) continue;

            jobs[i] = rule.PlanScript(entry, number);
            expressions.AddRange(jobs[i]!.Expressions);
        }

        var results = expressions.Count == 0 ? [] : rule.ScriptEngine.Evaluate(expressions);

        var items = new List<RenameItem>(entries.Count);
        int next = 0;
        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            if (jobs[i] is not { } job)
            {
                items.Add(MakeItem(entry, options.NameOverrides![entry.FullPath], options.Kind, options.TargetFolder, shortNames));
                continue;
            }

            var mine = results.Skip(next).Take(job.Expressions.Count).ToList();
            next += job.Expressions.Count;
            var (name, error) = job.Finish(mine);
            items.Add(error is not null
                ? new RenameItem(entry, entry.Name, RenameStatus.Error, error)
                : MakeItem(entry, name, options.Kind, options.TargetFolder, shortNames));
        }
        return items;
    }

    /// <summary>Checks one produced name (which may hold a relative path) and decides where the result goes.</summary>
    /// <param name="shortNames">When given, the final name part is converted to 8.3 (the registry keeps the <c>~N</c> counters).</param>
    internal static RenameItem MakeItem(
        FileEntry entry, string produced, TransferKind kind, string? targetFolder, ShortNameRegistry? shortNames = null)
    {
        var (name, directory, problem) = DestinationResolver.Resolve(entry, produced, kind, targetFolder);
        if (problem is null && shortNames is not null && name.Length > 0)
            name = shortNames.Convert(name, entry.IsDirectory, directory);
        bool sameFolder = DestinationResolver.SameFolder(directory, entry.Directory);
        string? destination = sameFolder ? null : directory;

        problem ??= DestinationResolver.Forbidden(entry, kind, directory);
        if (problem is not null)
            return new RenameItem(entry, produced, RenameStatus.InvalidName, problem, destination, kind);

        if (kind == TransferKind.Rename && sameFolder && name == entry.Name)
            return new RenameItem(entry, name, RenameStatus.Unchanged);

        return new RenameItem(entry, name, RenameStatus.Ready, null, destination, kind);
    }

    internal static void ResolveConflicts(List<RenameItem> items, RenameOptions options, PlanOptions plan)
    {
        var list = plan.ListDirectory ?? DefaultListDirectory;

        // Names in use per folder: whatever is on disk, minus items that leave their folder under a new name.
        var taken = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        HashSet<string> Names(string directory)
        {
            string key = directory.TrimEnd('\\', '/');
            if (!taken.TryGetValue(key, out var set))
                taken[key] = set = new HashSet<string>(list(directory), StringComparer.OrdinalIgnoreCase);
            return set;
        }

        foreach (var item in items.Where(i => i.Status == RenameStatus.Ready && i.Kind == TransferKind.Rename))
            Names(item.Entry.Directory).Remove(item.Entry.Name);

        for (int slot = 0; slot < items.Count; slot++)
        {
            var item = items[slot];
            if (item.Status == RenameStatus.Unchanged)
            {
                Names(item.Entry.Directory).Add(item.NewName);
                continue;
            }
            if (item.Status != RenameStatus.Ready) continue;

            var set = Names(item.TargetDirectory);
            string name = item.NewName;
            if (set.Contains(name) && plan.Conflict == ConflictPolicy.AutoNumber)
                name = FindFreeName(item, set, options, plan);

            items[slot] = set.Add(name)
                ? item with { NewName = name }
                : item with { Status = RenameStatus.Conflict, Message = "Name is already in use." };
        }
    }

    private static string FindFreeName(RenameItem item, HashSet<string> taken, RenameOptions options, PlanOptions plan)
    {
        var renamed = item.Entry with { Name = item.NewName };
        for (int n = plan.DuplicateStart; n < plan.DuplicateStart + 10_000; n++)
        {
            string suffix = ReplacementTemplate.Expand(
                plan.DuplicateFormat,
                new ExpansionContext(renamed, [], n, options.DateSource, options.Culture));

            string candidate = renamed.Extension.Length == 0
                ? renamed.Title + suffix
                : $"{renamed.Title}{suffix}.{renamed.Extension}";

            if (!taken.Contains(candidate)) return candidate;
        }
        return item.NewName;
    }

    private static IEnumerable<string> DefaultListDirectory(string directory) =>
        Directory.Exists(directory)
            ? Directory.EnumerateFileSystemEntries(directory).Select(Path.GetFileName).OfType<string>()
            : [];
}

/// <summary>Hands out the value of the <c>?</c> counter: one number per item, optionally restarting in every folder.</summary>
internal sealed class NumberSequence(RenameOptions options)
{
    private readonly Dictionary<string, int> _perFolder = new(StringComparer.OrdinalIgnoreCase);
    private readonly bool _restarts = options.ResetNumberPerFolder || options.ResetNumberPerDate
        || !string.IsNullOrEmpty(options.ResetNumberPerTag);
    private int _overall;

    public int Next(FileEntry entry) => Next(entry, NumberGroups.Key(entry, options, entry.Directory));

    /// <param name="group">The group key (see <see cref="NumberGroups.Key"/>) whose counter is used when numbering restarts.</param>
    public int Next(FileEntry entry, string group)
    {
        int index;
        if (_restarts)
        {
            _perFolder.TryGetValue(group, out index);
            _perFolder[group] = index + 1;
        }
        else
        {
            index = _overall++;
        }

        return options.NumberStart + index * options.NumberStep;
    }
}

/// <summary>Decides which items are counted together when the <c>?</c> counter restarts (folder, day, tag value).</summary>
internal static class NumberGroups
{
    /// <summary>
    /// Group key = folder (only when restarting per folder) + day of the file date (per date) + tag value (per tag).
    /// With none of the three on, every item has the same key (one group).
    /// </summary>
    public static string Key(FileEntry entry, RenameOptions options, string folder)
    {
        string dir = options.ResetNumberPerFolder ? folder.TrimEnd('\\', '/') : "";
        string day = options.ResetNumberPerDate
            ? (options.DateSource == DateSource.Created ? entry.Created : entry.Modified).Date.ToString("yyyyMMdd", CultureInfo.InvariantCulture)
            : "";
        string tag = "";
        if (!string.IsNullOrEmpty(options.ResetNumberPerTag))
        {
            var reader = options.TagReader ?? FileTagReader.Default;
            if (reader.Read(entry).Values.TryGetValue(options.ResetNumberPerTag, out var value))
                tag = value.Text ?? "";
        }
        return dir + "|" + day + "|" + tag;
    }
}

public static class NameValidator
{
    private static readonly char[] Illegal = ['\\', '/', ':', '*', '?', '"', '<', '>', '|'];

    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>Replaces characters a file name cannot contain (for example the slash in "AC/DC") with an underscore.</summary>
    public static string CleanForFileName(string text)
    {
        var chars = text.Select(c => Illegal.Contains(c) || char.IsControl(c) ? '_' : c).ToArray();
        return new string(chars).Trim().TrimEnd('.');
    }

    /// <returns>A description of the problem, or null when the name is legal.</returns>
    public static string? Check(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Name is empty.";
        if (name.Length > 255) return "Name is longer than 255 characters.";
        if (name.IndexOfAny(Illegal) >= 0 || name.Any(char.IsControl))
            return "Name contains one of \\ / : * ? \" < > |";
        if (name.EndsWith('.') || name.EndsWith(' ')) return "Name cannot end with a dot or space.";

        int dot = name.IndexOf('.');
        string stem = dot < 0 ? name : name[..dot];
        if (Reserved.Contains(stem.TrimEnd())) return $"'{stem}' is a reserved Windows name.";

        return null;
    }
}
