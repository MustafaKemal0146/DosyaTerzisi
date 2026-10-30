using System.Globalization;

namespace DosyaTerzisi.Core;

public enum RenameMethod
{
    /// <summary><c>*</c> and <c>?</c> match the whole name; each wildcard becomes <c>\1</c>..<c>\9</c>.</summary>
    Wildcard = 0,

    /// <summary>Regular expression, replaces every match inside the name.</summary>
    RegularExpression = 1,

    /// <summary>Character list to character list mapping, applied to the name without extension.</summary>
    Translation = 2,
}

public enum DateSource
{
    Modified,
    Created,
}

public sealed class RenameOptions
{
    public RenameMethod Method { get; set; } = RenameMethod.Wildcard;

    public string Search { get; set; } = "*";

    public string Replace { get; set; } = "";

    public bool CaseSensitive { get; set; }

    /// <summary>Value of the first <c>?</c> counter.</summary>
    public int NumberStart { get; set; } = 1;

    public int NumberStep { get; set; } = 1;

    /// <summary>Restart the counter in every folder.</summary>
    public bool ResetNumberPerFolder { get; set; }

    /// <summary>Restart the counter for every calendar day of the file date (<see cref="DateSource"/>).</summary>
    public bool ResetNumberPerDate { get; set; }

    /// <summary>Tag name such as <c>ID3.Album</c>: items with the same value are counted together (none = own group).</summary>
    public string? ResetNumberPerTag { get; set; }

    /// <summary>Reads <see cref="ResetNumberPerTag"/>; null = <see cref="FileTagReader.Default"/>.</summary>
    public ITagReader? TagReader { get; set; }

    /// <summary>Decimal, hexadecimal or letters.</summary>
    public NumberFormat NumberFormat { get; set; } = NumberFormat.Decimal;

    /// <summary>Which file date <c>\Y\m\d\H\M\S</c> read.</summary>
    public DateSource DateSource { get; set; } = DateSource.Modified;

    /// <summary>
    /// Names typed by hand for single items (F2 in the list), keyed by full path. They replace the rule's result;
    /// the counter still advances so the other names do not shift.
    /// </summary>
    public IReadOnlyDictionary<string, string>? NameOverrides { get; set; }

    /// <summary>Rename (default), copy, create a shortcut or create a hard link under the new name.</summary>
    public TransferKind Kind { get; set; } = TransferKind.Rename;

    /// <summary>
    /// Folder that relative paths in the new name start from, and where copies and links go.
    /// Empty = each item's own folder.
    /// </summary>
    public string? TargetFolder { get; set; }

    /// <summary>Shorten the new name to the DOS 8.3 shape (name up to 8, extension up to 3 characters; <c>~1</c> for cut names).</summary>
    public bool ShortNames83 { get; set; }

    /// <summary>The replacement text is a script expression (VBScript/JScript) whose value becomes the new name.</summary>
    public bool UseScript { get; set; }

    /// <summary>Evaluates the expressions when <see cref="UseScript"/> is on; null = the default script folder.</summary>
    public ScriptEngine? Script { get; set; }

    /// <summary>Culture used by <c>\u \U \l \L</c>. Use tr-TR in the UI so i/İ and ı/I work.</summary>
    public CultureInfo Culture { get; set; } = CultureInfo.InvariantCulture;
}
