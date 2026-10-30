using System.Globalization;
using System.Text;

namespace DosyaTerzisi.Core;

public enum ObjectKind
{
    /// <summary>A new file of the given size, filled with zeros.</summary>
    EmptyFile,

    CopyOfFile,

    EmptyFolder,

    CopyOfFolder,
}

public sealed class NumberedObjectOptions
{
    public ObjectKind Kind { get; set; } = ObjectKind.EmptyFile;

    /// <summary>Folder the new items are created in.</summary>
    public string Folder { get; set; } = "";

    /// <summary>
    /// Name with <c>?</c> where the number goes (<c>??</c> = two digits). Without a <c>?</c> and with more than one item,
    /// the number is added before the extension.
    /// </summary>
    public string NamePattern { get; set; } = "Yeni ?";

    public int Amount { get; set; } = 1;

    public int Start { get; set; } = 1;

    public int Step { get; set; } = 1;

    public NumberFormat NumberFormat { get; set; } = NumberFormat.Decimal;

    /// <summary>Size of each <see cref="ObjectKind.EmptyFile"/>, in bytes.</summary>
    public long SizeBytes { get; set; }

    /// <summary>File or folder to copy for <see cref="ObjectKind.CopyOfFile"/> and <see cref="ObjectKind.CopyOfFolder"/>.</summary>
    public string SourcePath { get; set; } = "";
}

public enum ObjectStatus
{
    Ready,

    /// <summary>A file or folder with this name is already there; it is never overwritten.</summary>
    Exists,

    InvalidName,
}

public sealed record PlannedObject(string Name, string FullPath, ObjectStatus Status, string? Message = null);

/// <summary>The names that will be created. <see cref="Problem"/> is set when nothing can be created at all.</summary>
public sealed record NumberedObjectPlan(IReadOnlyList<PlannedObject> Items, string? Problem)
{
    public bool CanCreate => Problem is null && Items.Any(i => i.Status == ObjectStatus.Ready);
}

public sealed record CreationFailure(string Path, string Message);

public sealed record CreationResult(IReadOnlyList<string> Created, IReadOnlyList<CreationFailure> Failures);

/// <summary>Reads sizes such as <c>512</c>, <c>10K</c>, <c>1.5M</c>, <c>2G</c> (powers of 1024).</summary>
public static class SizeParser
{
    public static bool TryParse(string? text, out long bytes)
    {
        bytes = 0;
        text = text?.Trim().Replace(" ", "");
        if (string.IsNullOrEmpty(text)) return false;

        text = text.TrimEnd('B', 'b');
        long unit = 1;
        if (text.Length > 0 && "KMGTkmgt".Contains(text[^1]))
        {
            unit = char.ToUpperInvariant(text[^1]) switch
            {
                'K' => 1L << 10,
                'M' => 1L << 20,
                'G' => 1L << 30,
                _ => 1L << 40,
            };
            text = text[..^1];
        }

        if (!double.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double number)
            || number < 0
            || double.IsNaN(number))
            return false;

        double total = number * unit;
        if (total > long.MaxValue / 2) return false;

        bytes = (long)total;
        return true;
    }
}

/// <summary>Works out the names for <see cref="NumberedObjectOptions"/> without touching the disk.</summary>
public static class NumberedObjectPlanner
{
    public const int MaxAmount = 100_000;

    /// <param name="exists">Tells whether a path is taken. Defaults to the real file system; tests replace it.</param>
    /// <param name="freeSpace">Free bytes in the target folder, null when unknown. Defaults to asking the drive.</param>
    public static NumberedObjectPlan Build(
        NumberedObjectOptions options, Func<string, bool>? exists = null, Func<string, long?>? freeSpace = null)
    {
        exists ??= path => File.Exists(path) || Directory.Exists(path);
        freeSpace ??= FreeSpaceOf;

        if (Problem(options) is { } problem) return new NumberedObjectPlan([], problem);

        var items = new List<PlannedObject>(options.Amount);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int width = Math.Max(
            options.Start.ToString(CultureInfo.InvariantCulture).Length,
            LastNumber(options).ToString(CultureInfo.InvariantCulture).Length);

        for (int i = 0; i < options.Amount; i++)
        {
            string name = NameFor(options, options.Start + i * options.Step, width);
            string path = Path.Combine(options.Folder, name);

            if (NameValidator.Check(name) is { } invalid)
                items.Add(new PlannedObject(name, path, ObjectStatus.InvalidName, invalid));
            else if (!used.Add(name) || exists(path))
                items.Add(new PlannedObject(name, path, ObjectStatus.Exists, "Bu adda bir öğe zaten var."));
            else
                items.Add(new PlannedObject(name, path, ObjectStatus.Ready));
        }

        long perItem = options.Kind switch
        {
            ObjectKind.EmptyFile => options.SizeBytes,
            ObjectKind.CopyOfFile => new FileInfo(options.SourcePath).Length,
            ObjectKind.CopyOfFolder => FolderSize(options.SourcePath),
            _ => 0,
        };
        long needed = perItem * items.Count(i => i.Status == ObjectStatus.Ready);
        if (freeSpace(options.Folder) is { } free && needed > free)
            return new NumberedObjectPlan(items, $"Diskte yeterli yer yok: {needed:N0} bayt gerekiyor, {free:N0} bayt boş.");

        return new NumberedObjectPlan(items, null);
    }

    private static string? Problem(NumberedObjectOptions o)
    {
        if (!Directory.Exists(o.Folder)) return "Hedef klasör bulunamadı.";
        if (o.Amount < 1 || o.Amount > MaxAmount) return $"Adet 1 ile {MaxAmount:N0} arasında olmalı.";
        if (o.Start < 0) return "Başlangıç sayısı eksi olamaz.";
        if (o.Step < 1) return "Adım en az 1 olmalı.";
        if (string.IsNullOrWhiteSpace(o.NamePattern)) return "Bir ad yazın.";
        if (LastNumber(o) > int.MaxValue) return "Sayı çok büyük.";

        switch (o.Kind)
        {
            case ObjectKind.EmptyFile when o.SizeBytes < 0:
                return "Boyut eksi olamaz.";
            case ObjectKind.CopyOfFile when !File.Exists(o.SourcePath):
                return "Kopyalanacak dosya bulunamadı.";
            case ObjectKind.CopyOfFolder when !Directory.Exists(o.SourcePath):
                return "Kopyalanacak klasör bulunamadı.";
            case ObjectKind.CopyOfFolder when IsInside(o.Folder, o.SourcePath):
                return "Klasör kendi içine kopyalanamaz.";
        }
        return null;
    }

    private static long LastNumber(NumberedObjectOptions o) => o.Start + (long)(o.Amount - 1) * o.Step;

    private static string NameFor(NumberedObjectOptions o, int number, int autoWidth)
    {
        string pattern = o.NamePattern.Trim();
        var sb = new StringBuilder();
        bool hasMarker = pattern.Contains('?');

        for (int i = 0; i < pattern.Length; i++)
        {
            if (pattern[i] != '?')
            {
                sb.Append(pattern[i]);
                continue;
            }

            int end = i;
            while (end < pattern.Length && pattern[end] == '?') end++;
            sb.Append(NumberFormatting.Format(number, end - i, o.NumberFormat));
            i = end - 1;
        }

        string name = sb.ToString();
        if (hasMarker || o.Amount == 1) return name;

        // No marker: keep the names apart by numbering them before the extension.
        string counter = NumberFormatting.Format(number, autoWidth, o.NumberFormat);
        bool isFile = o.Kind is ObjectKind.EmptyFile or ObjectKind.CopyOfFile;
        int dot = isFile ? name.LastIndexOf('.') : -1;
        return dot > 0 ? $"{name[..dot]}{counter}{name[dot..]}" : name + counter;
    }

    private static bool IsInside(string folder, string ancestor)
    {
        string a = Path.GetFullPath(ancestor).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
        string f = Path.GetFullPath(folder).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
        return f.StartsWith(a, StringComparison.OrdinalIgnoreCase);
    }

    private static long FolderSize(string path) =>
        new DirectoryInfo(path).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);

    private static long? FreeSpaceOf(string folder)
    {
        try
        {
            return new DriveInfo(Path.GetPathRoot(Path.GetFullPath(folder))!).AvailableFreeSpace;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}

/// <summary>Creates the planned files and folders. Never overwrites anything.</summary>
public static class NumberedObjectCreator
{
    public static CreationResult Create(NumberedObjectOptions options, NumberedObjectPlan plan)
    {
        var created = new List<string>();
        var failures = new List<CreationFailure>();
        if (plan.Problem is not null) return new CreationResult(created, failures);

        foreach (var item in plan.Items.Where(i => i.Status == ObjectStatus.Ready))
        {
            try
            {
                CreateOne(options, item.FullPath);
                created.Add(item.FullPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failures.Add(new CreationFailure(item.FullPath, ex.Message));
            }
        }

        return new CreationResult(created, failures);
    }

    private static void CreateOne(NumberedObjectOptions options, string path)
    {
        switch (options.Kind)
        {
            case ObjectKind.EmptyFile:
                // CreateNew fails instead of replacing a file that appeared after the plan was made.
                using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
                    stream.SetLength(options.SizeBytes);
                break;

            case ObjectKind.CopyOfFile:
                File.Copy(options.SourcePath, path, overwrite: false);
                break;

            case ObjectKind.EmptyFolder:
                if (Directory.Exists(path) || File.Exists(path)) throw new IOException("Bu adda bir öğe zaten var.");
                Directory.CreateDirectory(path);
                break;

            case ObjectKind.CopyOfFolder:
                if (Directory.Exists(path) || File.Exists(path)) throw new IOException("Bu adda bir öğe zaten var.");
                CopyFolder(options.SourcePath, path);
                break;
        }
    }

    private static void CopyFolder(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(source))
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: false);
        foreach (var folder in Directory.EnumerateDirectories(source))
            CopyFolder(folder, Path.Combine(target, Path.GetFileName(folder)));
    }
}
