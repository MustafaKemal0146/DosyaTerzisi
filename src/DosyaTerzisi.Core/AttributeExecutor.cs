namespace DosyaTerzisi.Core;

public sealed record AttributeFailure(string Path, string Message);

/// <summary>The items that were changed, in the order they were changed. Pass it to <see cref="AttributeExecutor.Undo"/>.</summary>
public sealed class AttributeJournal(IReadOnlyList<AttributeItem> items)
{
    public IReadOnlyList<AttributeItem> Items { get; } = items;
}

public sealed record AttributeResult(AttributeJournal Journal, IReadOnlyList<AttributeFailure> Failures);

/// <summary>Writes planned attributes and times to the disk.</summary>
public static class AttributeExecutor
{
    public static AttributeResult Apply(IEnumerable<AttributeItem> items) => Run(items.Where(i => i.WillChange));

    /// <summary>Puts back the original attributes and times of everything in the journal (newest first).</summary>
    public static AttributeResult Undo(AttributeJournal journal) =>
        Run(journal.Items.Reverse().Select(i => i.Reverse()), recordInJournal: false);

    private static AttributeResult Run(IEnumerable<AttributeItem> items, bool recordInJournal = true)
    {
        var done = new List<AttributeItem>();
        var failures = new List<AttributeFailure>();

        foreach (var item in items)
        {
            string path = item.Entry.FullPath;
            try
            {
                Write(item, File.GetAttributes(path));
                if (recordInJournal) done.Add(item);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentOutOfRangeException)
            {
                failures.Add(new AttributeFailure(path, ex.Message));
            }
        }

        return new AttributeResult(new AttributeJournal(done), failures);
    }

    private static void Write(AttributeItem item, FileAttributes current)
    {
        string path = item.Entry.FullPath;
        bool folder = item.Entry.IsDirectory;

        try
        {
            // A read-only file refuses new times, so lift the flag first and set the wanted attributes at the end.
            if (current.HasFlag(FileAttributes.ReadOnly))
                File.SetAttributes(path, current & ~FileAttributes.ReadOnly);

            if (item.ChangeCreated)
            {
                if (folder) Directory.SetCreationTime(path, item.NewCreated);
                else File.SetCreationTime(path, item.NewCreated);
            }
            if (item.ChangeAccessed)
            {
                if (folder) Directory.SetLastAccessTime(path, item.NewAccessed);
                else File.SetLastAccessTime(path, item.NewAccessed);
            }
            if (item.ChangeModified)
            {
                if (folder) Directory.SetLastWriteTime(path, item.NewModified);
                else File.SetLastWriteTime(path, item.NewModified);
            }

            File.SetAttributes(path, item.ChangeAttributes ? item.NewAttributes : current);
        }
        catch
        {
            // Do not leave a file without its read-only flag just because a time could not be set.
            try { File.SetAttributes(path, current); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            throw;
        }
    }
}
