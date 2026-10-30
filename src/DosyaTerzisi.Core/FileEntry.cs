namespace DosyaTerzisi.Core;

/// <summary>A file or folder that takes part in a rename operation.</summary>
public sealed record FileEntry(
    string Directory,
    string Name,
    bool IsDirectory,
    DateTime Modified,
    DateTime Created)
{
    /// <summary>Size in bytes, 0 for folders.</summary>
    public long Size { get; init; }

    public FileAttributes Attributes { get; init; }

    /// <summary>Last access time; the same as <see cref="Modified"/> when the entry was not read from disk.</summary>
    public DateTime Accessed { get; init; }

    public string FullPath => Path.Combine(Directory, Name);

    /// <summary>Name without extension. Folders and dot-files keep their whole name.</summary>
    public string Title
    {
        get
        {
            int dot = ExtensionDot;
            return dot < 0 ? Name : Name[..dot];
        }
    }

    /// <summary>Extension without the leading dot, empty for folders.</summary>
    public string Extension
    {
        get
        {
            int dot = ExtensionDot;
            return dot < 0 ? "" : Name[(dot + 1)..];
        }
    }

    /// <summary>Name of the folder that contains this entry.</summary>
    public string FolderName => Path.GetFileName(Directory.TrimEnd('\\', '/'));

    /// <summary>Name of the folder that contains <see cref="FolderName"/>.</summary>
    public string ParentFolderName =>
        Path.GetFileName((Path.GetDirectoryName(Directory.TrimEnd('\\', '/')) ?? "").TrimEnd('\\', '/'));

    private int ExtensionDot
    {
        get
        {
            if (IsDirectory) return -1;
            int dot = Name.LastIndexOf('.');
            return dot > 0 ? dot : -1;
        }
    }

    /// <summary>Reads a file or folder from disk, or returns null when the path does not exist.</summary>
    public static FileEntry? FromPath(string path)
    {
        var dir = new DirectoryInfo(path);
        if (dir.Exists) return From(dir);

        var file = new FileInfo(path);
        return file.Exists ? From(file) : null;
    }

    internal static FileEntry From(DirectoryInfo dir) =>
        new(dir.Parent?.FullName ?? "", dir.Name, true, dir.LastWriteTime, dir.CreationTime)
        {
            Attributes = dir.Attributes,
            Accessed = dir.LastAccessTime,
        };

    internal static FileEntry From(FileInfo file) =>
        new(file.DirectoryName ?? "", file.Name, false, file.LastWriteTime, file.CreationTime)
        {
            Size = file.Length,
            Attributes = file.Attributes,
            Accessed = file.LastAccessTime,
        };
}
