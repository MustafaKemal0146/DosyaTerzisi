using System.Globalization;

namespace DosyaTerzisi.Core;

public sealed class ScanOptions
{
    public bool IncludeFiles { get; set; } = true;

    public bool IncludeFolders { get; set; }

    /// <summary>How many levels of subfolders to enter. 0 = only the chosen folder, <see cref="Unlimited"/> = all.</summary>
    public int MaxDepth { get; set; }

    public const int Unlimited = int.MaxValue;

    /// <summary>Wildcard filter such as <c>*.jpg;*.png</c>, applied to the names of the included files and folders.</summary>
    public string Filter { get; set; } = "*";

    /// <summary>Hidden files and folders are listed only when this is true.</summary>
    public bool IncludeHidden { get; set; } = true;

    /// <summary>Operating system files (System attribute) are listed only when this is true.</summary>
    public bool IncludeSystem { get; set; }

    public CultureInfo Culture { get; set; } = CultureInfo.InvariantCulture;
}

public sealed record ScanError(string Path, string Message);

public sealed record ScanResult(IReadOnlyList<FileEntry> Entries, IReadOnlyList<ScanError> Errors);

/// <summary>Lists the files and folders that a rename will work on.</summary>
public static class FileScanner
{
    /// <summary>
    /// Walks <paramref name="root"/>. Inside every folder the subfolders come first, then the files, each in
    /// natural order ("2" before "10"); the contents of a subfolder follow after its parent's own list.
    /// Folders that cannot be read are reported in <see cref="ScanResult.Errors"/> and skipped.
    /// Junctions and symbolic links are listed but never entered.
    /// </summary>
    /// <exception cref="DirectoryNotFoundException">The root folder does not exist.</exception>
    public static ScanResult Scan(string root, ScanOptions options, CancellationToken cancel = default)
    {
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException($"Folder not found: {root}");

        var scan = new Walker(options, cancel);
        scan.Walk(new DirectoryInfo(root), 0);
        return new ScanResult(scan.Entries, scan.Errors);
    }

    /// <summary>Reads explicitly chosen files and folders (drag and drop). Paths that do not exist are reported.</summary>
    public static ScanResult FromPaths(IEnumerable<string> paths)
    {
        var entries = new List<FileEntry>();
        var errors = new List<ScanError>();

        foreach (var path in paths)
        {
            try
            {
                var entry = FileEntry.FromPath(path);
                if (entry is null) errors.Add(new ScanError(path, "Not found."));
                else entries.Add(entry);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                errors.Add(new ScanError(path, ex.Message));
            }
        }

        return new ScanResult(entries, errors);
    }

    private sealed class Walker
    {
        private readonly ScanOptions _options;
        private readonly CancellationToken _cancel;
        private readonly NameFilter _filter;
        private readonly NaturalComparer _comparer;

        public List<FileEntry> Entries { get; } = [];
        public List<ScanError> Errors { get; } = [];

        public Walker(ScanOptions options, CancellationToken cancel)
        {
            _options = options;
            _cancel = cancel;
            _filter = NameFilter.Parse(options.Filter);
            _comparer = new NaturalComparer(options.Culture);
        }

        public void Walk(DirectoryInfo directory, int depth)
        {
            _cancel.ThrowIfCancellationRequested();

            DirectoryInfo[] folders;
            FileInfo[] files;
            try
            {
                folders = directory.GetDirectories();
                files = _options.IncludeFiles ? directory.GetFiles() : [];
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Errors.Add(new ScanError(directory.FullName, ex.Message));
                return;
            }

            Array.Sort(folders, (a, b) => _comparer.Compare(a.Name, b.Name));
            Array.Sort(files, (a, b) => _comparer.Compare(a.Name, b.Name));

            if (_options.IncludeFolders)
            {
                foreach (var folder in folders.Where(f => Accepts(f.Name, f.Attributes)))
                    Entries.Add(FileEntry.From(folder));
            }

            foreach (var file in files.Where(f => Accepts(f.Name, f.Attributes)))
                Entries.Add(FileEntry.From(file));

            if (depth >= _options.MaxDepth) return;

            foreach (var folder in folders)
            {
                // Hidden/system folders are not entered either, and links could loop back to a parent.
                if (!AllowedByAttributes(folder.Attributes)) continue;
                if (folder.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;

                Walk(folder, depth + 1);
            }
        }

        private bool Accepts(string name, FileAttributes attributes) =>
            AllowedByAttributes(attributes) && _filter.IsMatch(name);

        private bool AllowedByAttributes(FileAttributes attributes) =>
            (_options.IncludeHidden || !attributes.HasFlag(FileAttributes.Hidden))
            && (_options.IncludeSystem || !attributes.HasFlag(FileAttributes.System));
    }
}
