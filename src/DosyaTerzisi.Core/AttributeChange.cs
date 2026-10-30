namespace DosyaTerzisi.Core;

/// <summary>What to do with one file attribute. <see cref="Keep"/> leaves it as it is.</summary>
public enum Tri
{
    Keep,
    Set,
    Clear,
}

public enum TimestampKind
{
    Modified,
    Created,
    Accessed,
}

public enum TimestampAction
{
    Keep,

    /// <summary>Use <see cref="TimestampRule.Value"/> for every item.</summary>
    SetTo,

    /// <summary>Move the current time by <see cref="TimestampRule.Shift"/> (negative = earlier).</summary>
    Shift,

    /// <summary>Use another one of the item's own times, as it was before the change.</summary>
    CopyFrom,

    /// <summary>The date the photo was taken (EXIF). Files without one keep their time.</summary>
    PhotoTaken,

    /// <summary>A time between <see cref="TimestampRule.Value"/> and <see cref="TimestampRule.RandomTo"/>, different for every item.</summary>
    Random,
}

public sealed class TimestampRule
{
    public TimestampAction Action { get; set; } = TimestampAction.Keep;

    public DateTime Value { get; set; }

    public TimeSpan Shift { get; set; }

    public TimestampKind CopyFrom { get; set; }

    /// <summary>End of the range for <see cref="TimestampAction.Random"/>; <see cref="Value"/> is the start.</summary>
    public DateTime RandomTo { get; set; }

    /// <summary>
    /// Makes the random times repeatable: the same seed gives the same time to the same file, so the preview shows
    /// exactly what will be applied. Pick another seed to shuffle again.
    /// </summary>
    public int Seed { get; set; }

    /// <summary>Whatever the action gives is held inside these limits.</summary>
    public DateTime? Min { get; set; }

    public DateTime? Max { get; set; }

    public static TimestampRule SetTo(DateTime value) => new() { Action = TimestampAction.SetTo, Value = value };

    public static TimestampRule ShiftBy(TimeSpan shift) => new() { Action = TimestampAction.Shift, Shift = shift };

    public static TimestampRule CopyOf(TimestampKind kind) => new() { Action = TimestampAction.CopyFrom, CopyFrom = kind };

    public static TimestampRule FromPhotoTaken() => new() { Action = TimestampAction.PhotoTaken };

    public static TimestampRule RandomBetween(DateTime from, DateTime to, int seed = 0) =>
        new() { Action = TimestampAction.Random, Value = from, RandomTo = to, Seed = seed };
}

/// <summary>Attributes and file times to change.</summary>
public sealed class AttributeRule
{
    public Tri ReadOnly { get; set; }
    public Tri Archive { get; set; }
    public Tri Hidden { get; set; }
    public Tri System { get; set; }

    public TimestampRule Modified { get; set; } = new();
    public TimestampRule Created { get; set; } = new();
    public TimestampRule Accessed { get; set; } = new();

    /// <summary>When false, folders are left alone and only files change.</summary>
    public bool IncludeFolders { get; set; } = true;
}

public enum AttributeStatus
{
    Unchanged,
    Ready,

    /// <summary>The rule gives a time Windows cannot store.</summary>
    Error,
}

/// <summary>What one file or folder will look like after the change.</summary>
public sealed record AttributeItem(
    FileEntry Entry,
    FileAttributes NewAttributes,
    DateTime NewModified,
    DateTime NewCreated,
    DateTime NewAccessed,
    bool ChangeAttributes,
    bool ChangeModified,
    bool ChangeCreated,
    bool ChangeAccessed,
    AttributeStatus Status,
    string? Message = null)
{
    public bool WillChange => Status == AttributeStatus.Ready;

    /// <summary>The item that puts back what <see cref="Entry"/> had before.</summary>
    public AttributeItem Reverse() => this with
    {
        NewAttributes = Entry.Attributes,
        NewModified = Entry.Modified,
        NewCreated = Entry.Created,
        NewAccessed = Entry.Accessed,
    };
}

/// <summary>Works out the new attributes and times without touching the disk.</summary>
public static class AttributePlanner
{
    /// <summary>Earliest time a Windows file can have.</summary>
    public static readonly DateTime MinTime = new(1601, 1, 2);

    /// <param name="tags">Reads the photo-taken date for <see cref="TimestampAction.PhotoTaken"/>. Defaults to the real files.</param>
    public static IReadOnlyList<AttributeItem> Build(IEnumerable<FileEntry> entries, AttributeRule rule, ITagReader? tags = null)
    {
        tags ??= FileTagReader.Default;
        return entries
            .Where(e => rule.IncludeFolders || !e.IsDirectory)
            .Select(e => Plan(e, rule, tags))
            .ToList();
    }

    private static AttributeItem Plan(FileEntry entry, AttributeRule rule, ITagReader tags)
    {
        DateTime? taken = null;
        bool needsPhotoDate = new[] { rule.Modified, rule.Created, rule.Accessed }.Any(r => r.Action == TimestampAction.PhotoTaken);
        if (needsPhotoDate && !entry.IsDirectory
            && tags.Read(entry).Values.TryGetValue("EXIF.OriginalTime", out var photo))
            taken = photo.Date;

        var attributes = entry.Attributes;
        attributes = Apply(attributes, FileAttributes.ReadOnly, rule.ReadOnly);
        attributes = Apply(attributes, FileAttributes.Archive, rule.Archive);
        attributes = Apply(attributes, FileAttributes.Hidden, rule.Hidden);
        attributes = Apply(attributes, FileAttributes.System, rule.System);

        attributes = Normalize(attributes);
        bool changeAttributes = attributes != Normalize(entry.Attributes);

        var modified = Resolve(entry, rule.Modified, entry.Modified, taken);
        var created = Resolve(entry, rule.Created, entry.Created, taken);
        var accessed = Resolve(entry, rule.Accessed, entry.Accessed, taken);

        bool noPhotoDate = needsPhotoDate && taken is null;

        string? problem = Check("değiştirilme", rule.Modified, modified)
                          ?? Check("oluşturulma", rule.Created, created)
                          ?? Check("erişim", rule.Accessed, accessed);

        bool changeModified = rule.Modified.Action != TimestampAction.Keep && modified != entry.Modified;
        bool changeCreated = rule.Created.Action != TimestampAction.Keep && created != entry.Created;
        bool changeAccessed = rule.Accessed.Action != TimestampAction.Keep && accessed != entry.Accessed;

        var status = problem is not null ? AttributeStatus.Error
            : changeAttributes || changeModified || changeCreated || changeAccessed ? AttributeStatus.Ready
            : AttributeStatus.Unchanged;

        problem ??= noPhotoDate && !entry.IsDirectory ? "Çekim tarihi bulunamadı." : null;

        return new AttributeItem(
            entry, attributes, modified, created, accessed,
            changeAttributes, changeModified, changeCreated, changeAccessed, status, problem);
    }

    /// <summary>"Normal" only means "no other flag"; it cannot stay next to other flags, and no flags at all is "Normal".</summary>
    private static FileAttributes Normalize(FileAttributes attributes)
    {
        attributes &= ~FileAttributes.Normal;
        return attributes == 0 ? FileAttributes.Normal : attributes;
    }

    private static FileAttributes Apply(FileAttributes attributes, FileAttributes flag, Tri change) => change switch
    {
        Tri.Set => attributes | flag,
        Tri.Clear => attributes & ~flag,
        _ => attributes,
    };

    private static DateTime Resolve(FileEntry entry, TimestampRule rule, DateTime current, DateTime? photoTaken)
    {
        try
        {
            var result = rule.Action switch
            {
                TimestampAction.SetTo => rule.Value,
                TimestampAction.Random => RandomTime(entry, rule),
                TimestampAction.Shift => current + rule.Shift,
                TimestampAction.PhotoTaken => photoTaken ?? current,
                TimestampAction.CopyFrom => rule.CopyFrom switch
                {
                    TimestampKind.Created => entry.Created,
                    TimestampKind.Accessed => entry.Accessed,
                    _ => entry.Modified,
                },
                _ => current,
            };

            if (rule.Action != TimestampAction.Keep)
            {
                if (rule.Min is { } min && result < min) result = min;
                if (rule.Max is { } max && result > max) result = max;
            }
            return result;
        }
        catch (ArgumentOutOfRangeException)
        {
            return DateTime.MinValue;
        }
    }

    private static DateTime RandomTime(FileEntry entry, TimestampRule rule)
    {
        var (from, to) = rule.RandomTo >= rule.Value ? (rule.Value, rule.RandomTo) : (rule.RandomTo, rule.Value);
        long seconds = (long)(to - from).TotalSeconds;
        if (seconds <= 0) return from;

        var hash = System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes($"{rule.Seed}|{entry.FullPath.ToLowerInvariant()}"));
        ulong pick = BitConverter.ToUInt64(hash, 0) % (ulong)(seconds + 1);
        return from.AddSeconds(pick);
    }

    private static string? Check(string name, TimestampRule rule, DateTime value) =>
        rule.Action != TimestampAction.Keep && value < MinTime
            ? $"Yeni {name} tarihi geçersiz (en erken 1601 olabilir)."
            : null;
}
