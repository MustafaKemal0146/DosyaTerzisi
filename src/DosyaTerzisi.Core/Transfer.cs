using System.ComponentModel;
using System.Runtime.InteropServices;

namespace DosyaTerzisi.Core;

/// <summary>What happens to the item under its new name.</summary>
public enum TransferKind
{
    /// <summary>The item itself gets the new name (and moves, when the new name leads into another folder).</summary>
    Rename,

    /// <summary>A copy is made under the new name; the original stays.</summary>
    Copy,

    /// <summary>A Windows shortcut (.lnk) to the item is created under the new name.</summary>
    Shortcut,

    /// <summary>A hard link to the file is created under the new name (same drive, files only).</summary>
    Hardlink,
}

/// <summary>
/// Turns what a rule produced into a place on disk. The produced text may contain a relative path:
/// <c>/</c> goes into a sub folder (created when needed), <c>..</c> goes to the parent folder:
/// <c>../\Y-\m/\0</c> moves a file one level up into a folder named after its date.
/// </summary>
internal static class DestinationResolver
{
    /// <param name="baseFolder">Where relative paths start; the item's own folder when empty.</param>
    public static (string Name, string Directory, string? Problem) Resolve(
        FileEntry entry, string produced, TransferKind kind, string? baseFolder)
    {
        string directory = string.IsNullOrWhiteSpace(baseFolder) ? entry.Directory : baseFolder.Trim();
        string[] parts = produced.Split('/');

        for (int i = 0; i < parts.Length - 1; i++)
        {
            string part = parts[i];
            if (part.Length == 0 || part == ".") continue;

            if (part == "..")
            {
                directory = Path.GetDirectoryName(directory.TrimEnd('\\', '/')) ?? directory;
                continue;
            }

            if (NameValidator.Check(part) is { } folderProblem) return (produced, directory, $"Klasör adı geçersiz ({part}): {folderProblem}");
            directory = Path.Combine(directory, part);
        }

        string name = parts[^1];
        if (kind == TransferKind.Shortcut && name.Length > 0 && !name.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            name += ".lnk";

        return (name, SafeFull(directory), NameValidator.Check(name));
    }

    /// <summary>Reasons an item can never be transferred, whatever its name.</summary>
    public static string? Forbidden(FileEntry entry, TransferKind kind, string directory)
    {
        if (kind == TransferKind.Hardlink && entry.IsDirectory) return "Klasör için sabit bağ oluşturulamaz.";

        if (entry.IsDirectory && IsInside(directory, entry.FullPath))
            return kind == TransferKind.Rename ? "Klasör kendi içine taşınamaz." : "Klasör kendi içine kopyalanamaz.";

        return null;
    }

    public static bool SameFolder(string a, string b) =>
        string.Equals(SafeFull(a).TrimEnd('\\', '/'), SafeFull(b).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);

    public static bool IsInside(string path, string ancestor)
    {
        string p = SafeFull(path).TrimEnd('\\', '/');
        string a = SafeFull(ancestor).TrimEnd('\\', '/');
        return p.Equals(a, StringComparison.OrdinalIgnoreCase)
            || p.StartsWith(a + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static string SafeFull(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return path;
        }
    }
}

/// <summary>Copies, moves into other folders, creates shortcuts and hard links, and takes them back. Never overwrites.</summary>
internal static class TransferExecutor
{
    public static ExecutionResult Execute(IEnumerable<RenameItem> items, string tag)
    {
        var done = new List<RenameMove>();
        var failures = new List<RenameFailure>();

        foreach (var item in items)
        {
            string toDirectory = item.DestinationDirectory ?? item.Entry.Directory;
            string from = item.Entry.FullPath;
            string to = Path.Combine(toDirectory, item.NewName);

            try
            {
                var created = MissingFolders(toDirectory);
                Directory.CreateDirectory(toDirectory);

                switch (item.Kind)
                {
                    case TransferKind.Rename:
                        if (item.Entry.IsDirectory) Directory.Move(from, to);
                        else File.Move(from, to);
                        break;
                    case TransferKind.Copy:
                        if (item.Entry.IsDirectory) CopyFolder(from, toDirectory, item.NewName, tag);
                        else CopyFile(from, toDirectory, item.NewName, tag);
                        break;
                    case TransferKind.Shortcut:
                        CreateShortcut(from, to, item.Entry.IsDirectory);
                        break;
                    case TransferKind.Hardlink:
                        CreateHardLink(from, to);
                        break;
                }

                done.Add(new RenameMove(item.Entry.Directory, item.Entry.Name, item.NewName, item.Entry.IsDirectory, toDirectory, item.Kind, created));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception or COMException or NotSupportedException)
            {
                failures.Add(new RenameFailure(from, to, ex.Message));
            }
        }

        return new ExecutionResult(new RenameJournal(done), failures);
    }

    /// <summary>Takes back transfers, last one first.</summary>
    public static ExecutionResult Undo(IEnumerable<RenameMove> moves)
    {
        var undone = new List<RenameMove>();
        var failures = new List<RenameFailure>();

        foreach (var move in moves.Reverse())
        {
            string toDirectory = move.TargetDirectory ?? move.Directory;
            string from = Path.Combine(move.Directory, move.OldName);
            string to = Path.Combine(toDirectory, move.NewName);

            try
            {
                switch (move.Kind)
                {
                    case TransferKind.Rename:
                        if (move.IsDirectory) Directory.Move(to, from);
                        else File.Move(to, from);
                        break;
                    case TransferKind.Copy:
                        RemoveCopy(from, to, move.IsDirectory);
                        break;
                    case TransferKind.Shortcut:
                    case TransferKind.Hardlink:
                        if (File.Exists(to)) File.Delete(to);
                        break;
                }

                foreach (string folder in (move.CreatedDirectories ?? []).Reverse())
                    TryDeleteEmpty(folder);

                // Like a plain undo, a moved item is reported in the direction it just went (back to where it came from).
                undone.Add(move.Kind == TransferKind.Rename
                    ? move with { Directory = toDirectory, OldName = move.NewName, NewName = move.OldName, TargetDirectory = move.Directory }
                    : move);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failures.Add(new RenameFailure(to, move.OldName, ex.Message));
            }
        }

        undone.Reverse();
        return new ExecutionResult(new RenameJournal(undone), failures);
    }

    // ---- Copying -------------------------------------------------------------------------------------------------

    private static void CopyFile(string source, string directory, string name, string tag)
    {
        string final = Path.Combine(directory, name);
        if (File.Exists(final) || Directory.Exists(final)) throw new IOException($"'{name}' zaten var.");

        // Copy under a temporary name first: a half-written file never carries the final name.
        string temp = Path.Combine(directory, RenameExecutor.TempName(tag, name));
        try
        {
            File.Copy(source, temp, overwrite: false);
            File.Move(temp, final, overwrite: false);
        }
        catch
        {
            TryDeleteFile(temp);
            throw;
        }
    }

    private static void CopyFolder(string source, string directory, string name, string tag)
    {
        string final = Path.Combine(directory, name);
        if (File.Exists(final) || Directory.Exists(final)) throw new IOException($"'{name}' zaten var.");

        string temp = Path.Combine(directory, RenameExecutor.TempName(tag, name));
        try
        {
            CopyTree(source, temp);
            Directory.Move(temp, final);
        }
        catch
        {
            try
            {
                if (Directory.Exists(temp)) Directory.Delete(temp, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
            throw;
        }
    }

    private static void CopyTree(string source, string target)
    {
        Directory.CreateDirectory(target);
        var info = new DirectoryInfo(source);

        foreach (var file in info.EnumerateFiles())
            File.Copy(file.FullName, Path.Combine(target, file.Name), overwrite: false);

        foreach (var sub in info.EnumerateDirectories())
        {
            if (sub.Attributes.HasFlag(FileAttributes.ReparsePoint))
                throw new IOException($"'{sub.Name}' bir bağlantı noktası; kopyalanmadı.");
            CopyTree(sub.FullName, Path.Combine(target, sub.Name));
        }
    }

    /// <summary>
    /// Removes a copy this program made, but only parts that still match the original (same size and time).
    /// Anything that changed since stays; the person decides what to do with it.
    /// </summary>
    private static void RemoveCopy(string source, string copy, bool isDirectory)
    {
        if (!isDirectory)
        {
            if (!File.Exists(copy)) return;
            if (!SameFile(source, copy)) throw new IOException("Kopya değiştirilmiş ya da özgün dosya yok; silinmedi.");
            File.Delete(copy);
            return;
        }

        if (!Directory.Exists(copy)) return;
        int kept = RemoveMatching(source, copy);
        if (kept > 0) throw new IOException($"{kept} öğe özgünden farklı ya da özgünü yok; silinmedi.");
    }

    private static int RemoveMatching(string source, string copy)
    {
        int kept = 0;
        var info = new DirectoryInfo(copy);

        foreach (var file in info.EnumerateFiles())
        {
            string original = Path.Combine(source, file.Name);
            if (SameFile(original, file.FullName)) file.Delete();
            else kept++;
        }

        foreach (var sub in info.EnumerateDirectories())
            kept += RemoveMatching(Path.Combine(source, sub.Name), sub.FullName);

        if (kept == 0) info.Delete();
        return kept;
    }

    private static bool SameFile(string a, string b)
    {
        var x = new FileInfo(a);
        var y = new FileInfo(b);
        return x.Exists && y.Exists && x.Length == y.Length && x.LastWriteTimeUtc == y.LastWriteTimeUtc;
    }

    // ---- Shortcuts and links -------------------------------------------------------------------------------------

    private static void CreateShortcut(string target, string shortcutPath, bool targetIsFolder)
    {
        // The shell would silently replace an existing shortcut; this program never overwrites.
        if (File.Exists(shortcutPath) || Directory.Exists(shortcutPath)) throw new IOException($"'{Path.GetFileName(shortcutPath)}' zaten var.");

        if (!OperatingSystem.IsWindows()) throw new IOException("Kısayol oluşturma yalnızca Windows'ta kullanılabilir.");

        var shellType = Type.GetTypeFromProgID("WScript.Shell") ?? throw new IOException("Kısayol oluşturma bu bilgisayarda kullanılamıyor.");
        object shell = Activator.CreateInstance(shellType)!;
        object? link = null;
        try
        {
            dynamic s = shell;
            link = s.CreateShortcut(shortcutPath);
            dynamic l = link!;
            l.TargetPath = target;
            l.WorkingDirectory = targetIsFolder ? target : Path.GetDirectoryName(target) ?? "";
            l.Save();
        }
        finally
        {
            if (link is not null) Marshal.ReleaseComObject(link);
            Marshal.ReleaseComObject(shell);
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateHardLinkW(string newFileName, string existingFileName, IntPtr securityAttributes);

    private static void CreateHardLink(string existing, string link)
    {
        if (File.Exists(link) || Directory.Exists(link)) throw new IOException($"'{Path.GetFileName(link)}' zaten var.");
        if (!CreateHardLinkW(link, existing, IntPtr.Zero))
            throw new IOException(new Win32Exception(Marshal.GetLastWin32Error()).Message);
    }

    // ---- Folders -------------------------------------------------------------------------------------------------

    /// <summary>The folders that do not exist yet on the way to <paramref name="directory"/>, outermost first.</summary>
    private static IReadOnlyList<string> MissingFolders(string directory)
    {
        var missing = new List<string>();
        string? current = directory.TrimEnd('\\', '/');
        while (!string.IsNullOrEmpty(current) && !Directory.Exists(current))
        {
            missing.Add(current);
            current = Path.GetDirectoryName(current);
        }
        missing.Reverse();
        return missing;
    }

    private static void TryDeleteEmpty(string folder)
    {
        try
        {
            if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any()) Directory.Delete(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
