using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.Iptc;
using TagLib;
using File = System.IO.File;

namespace DosyaTerzisi.Core;

/// <summary>One value read from a file: its text, and the date or number behind it when it has one (for formatting).</summary>
public sealed record TagValue(string Text, DateTime? Date = null, double? Number = null);

/// <param name="Values">Tags by name, such as <c>ID3.Artist</c> or <c>EXIF.OriginalTime</c>. Names ignore case.</param>
/// <param name="Error">Set when the file could not be read (damaged, locked); <paramref name="Values"/> is empty then.</param>
public sealed record TagReadResult(IReadOnlyDictionary<string, TagValue> Values, string? Error = null)
{
    public static readonly TagReadResult Empty = new(new Dictionary<string, TagValue>());
}

public interface ITagReader
{
    TagReadResult Read(FileEntry entry);
}

/// <summary>
/// Reads music tags (<c>ID3.*</c>, <c>ID3v1.*</c>, <c>ID3v2.*</c>; also Ogg, FLAC, WMA, M4A...) and picture tags
/// (<c>EXIF.*</c>, <c>IPTC.*</c>) with their usual names, plus document properties (<c>DOC.*</c>)
/// and web page tags (<c>HTML.*</c>).
/// </summary>
public sealed class FileTagReader : ITagReader
{
    private static readonly HashSet<string> Music = new(StringComparer.OrdinalIgnoreCase)
    {
        "mp3", "wma", "ogg", "oga", "opus", "flac", "m4a", "mp4", "aac", "ape", "mpc", "mp+", "wv", "wav", "aif", "aiff",
    };

    private static readonly HashSet<string> Pictures = new(StringComparer.OrdinalIgnoreCase)
    {
        "jpg", "jpe", "jpeg", "tif", "tiff", "png", "psd", "heic", "webp",
    };

    /// <summary>Without the Windows shell properties: fast, used for lists and previews.</summary>
    public static readonly FileTagReader Default = new();

    /// <summary>
    /// Also asks the Windows shell for <c>PROP.*</c> tags. That costs 0.1-0.3 s per file, so it is only used when
    /// a <c>PROP.</c> tag is really asked for (a tag template that contains one, the tags of one file).
    /// </summary>
    public static readonly FileTagReader WithShellProperties = new(includeShellProperties: true);

    private readonly bool _shellProperties;

    public FileTagReader(bool includeShellProperties = false) => _shellProperties = includeShellProperties;

    public TagReadResult Read(FileEntry entry)
    {
        if (entry.IsDirectory) return TagReadResult.Empty;

        bool music = Music.Contains(entry.Extension);
        bool picture = Pictures.Contains(entry.Extension);
        bool document = DocumentTags.Handles(entry.Extension);

        var values = new Dictionary<string, TagValue>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (music) ReadMusic(entry.FullPath, values);
            else if (picture) ReadPicture(entry.FullPath, values);
            else if (document) DocumentTags.Read(entry.FullPath, entry.Extension, values);

            // Windows shell properties (PROP.*) for every kind of file; they never replace the names above.
            if (_shellProperties) PropertyTags.Read(entry.FullPath, values);
            return new TagReadResult(values);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CorruptFileException
                                      or UnsupportedFormatException or ImageProcessingException
                                      or System.IO.InvalidDataException or System.Xml.XmlException)
        {
            return new TagReadResult(new Dictionary<string, TagValue>(), ex.Message);
        }
    }

    // ---- Music ----------------------------------------------------------------------------------------------

    private static void ReadMusic(string path, Dictionary<string, TagValue> values)
    {
        using var file = TagLib.File.Create(path);
        var tag = file.Tag;

        AddText(values, "ID3.Artist", Joined(tag.Performers));
        AddText(values, "ID3.AlbumArtist", Joined(tag.AlbumArtists));
        AddText(values, "ID3.Composer", Joined(tag.Composers));
        AddText(values, "ID3.Album", tag.Album);
        AddText(values, "ID3.Track", tag.Title);
        AddText(values, "ID3.Title", tag.Title);
        AddText(values, "ID3.Comment", tag.Comment);
        AddText(values, "ID3.Genre", Joined(tag.Genres));
        AddNumber(values, "ID3.TrackNumber", tag.Track);
        AddNumber(values, "ID3.Disc", tag.Disc);
        AddNumber(values, "ID3.Year", tag.Year);

        if (file.GetTag(TagTypes.Id3v1) is { } v1)
        {
            AddText(values, "ID3v1.Artist", Joined(v1.Performers));
            AddText(values, "ID3v1.Album", v1.Album);
            AddText(values, "ID3v1.Track", v1.Title);
            AddText(values, "ID3v1.Comment", v1.Comment);
            AddText(values, "ID3v1.Genre", Joined(v1.Genres));
            AddNumber(values, "ID3v1.TrackNumber", v1.Track);
            AddNumber(values, "ID3v1.Year", v1.Year);
        }

        if (file.GetTag(TagTypes.Id3v2) is TagLib.Id3v2.Tag v2)
        {
            foreach (var frame in v2.GetFrames())
            {
                string id = frame.FrameId.ToString();
                switch (frame)
                {
                    case TagLib.Id3v2.TextInformationFrame text:
                        AddText(values, $"ID3v2.{id}", Joined(text.Text));
                        break;
                    case TagLib.Id3v2.CommentsFrame comment:
                        AddText(values, $"ID3v2.{id}", comment.Text);
                        break;
                }
            }
        }
    }

    /// <summary>
    /// ID3v2.3 separates several values with a slash, so a band called "AC/DC" is read back as two values.
    /// Joining them with the slash again gives the name that was typed.
    /// </summary>
    private static string Joined(string[] values) => string.Join("/", values);

    private static void AddText(Dictionary<string, TagValue> values, string name, string? text)
    {
        if (!string.IsNullOrWhiteSpace(text)) values.TryAdd(name, new TagValue(text.Trim()));
    }

    private static void AddNumber(Dictionary<string, TagValue> values, string name, uint number)
    {
        if (number > 0) values.TryAdd(name, new TagValue(number.ToString(), Number: number));
    }

    // ---- Pictures -------------------------------------------------------------------------------------------

    private static void ReadPicture(string path, Dictionary<string, TagValue> values)
    {
        var directories = ImageMetadataReader.ReadMetadata(path);

        if (directories.OfType<ExifIfd0Directory>().FirstOrDefault() is { } ifd0)
        {
            AddText(values, "EXIF.ImageDescription", ifd0.GetString(ExifDirectoryBase.TagImageDescription));
            AddText(values, "EXIF.Maker", ifd0.GetString(ExifDirectoryBase.TagMake));
            AddText(values, "EXIF.Model", ifd0.GetString(ExifDirectoryBase.TagModel));
            AddText(values, "EXIF.Software", ifd0.GetString(ExifDirectoryBase.TagSoftware));
            AddText(values, "EXIF.Artist", ifd0.GetString(ExifDirectoryBase.TagArtist));
            AddText(values, "EXIF.Copyright", ifd0.GetString(ExifDirectoryBase.TagCopyright));
        }

        if (directories.OfType<ExifSubIfdDirectory>().FirstOrDefault() is { } sub)
        {
            if (sub.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out var taken))
                values.TryAdd("EXIF.OriginalTime", new TagValue(taken.ToString("yyyy-MM-dd HH:mm:ss"), Date: taken));

            AddText(values, "EXIF.UserComment", sub.GetDescription(ExifDirectoryBase.TagUserComment));
            AddText(values, "EXIF.ColorSpace", sub.GetDescription(ExifDirectoryBase.TagColorSpace));
            AddText(values, "EXIF.Flash", sub.GetDescription(ExifDirectoryBase.TagFlash));
            AddText(values, "EXIF.FocalLength", sub.GetDescription(ExifDirectoryBase.TagFocalLength));
            AddText(values, "EXIF.F-Number", sub.GetDescription(ExifDirectoryBase.TagFNumber));
            AddText(values, "EXIF.ExposureTime", sub.GetDescription(ExifDirectoryBase.TagExposureTime));
            AddText(values, "EXIF.MeteringMode", sub.GetDescription(ExifDirectoryBase.TagMeteringMode));
            AddText(values, "EXIF.LightSource", sub.GetDescription(ExifDirectoryBase.TagWhiteBalance));
            AddText(values, "EXIF.ExposureProgram", sub.GetDescription(ExifDirectoryBase.TagExposureProgram));
            AddText(values, "EXIF.ExposureCompensation", sub.GetDescription(ExifDirectoryBase.TagExposureBias));
            if (sub.TryGetInt32(ExifDirectoryBase.TagIsoEquivalent, out int iso))
                values.TryAdd("EXIF.ISOSpeed", new TagValue(iso.ToString(), Number: iso));
        }

        if (directories.OfType<IptcDirectory>().FirstOrDefault() is { } iptc)
        {
            AddText(values, "IPTC.Caption", iptc.GetString(IptcDirectory.TagCaption));
            AddText(values, "IPTC.CaptionWriter", iptc.GetString(IptcDirectory.TagCaptionWriter));
            AddText(values, "IPTC.Headline", iptc.GetString(IptcDirectory.TagHeadline));
            AddText(values, "IPTC.Category", iptc.GetString(IptcDirectory.TagCategory));
            AddText(values, "IPTC.Byline", iptc.GetString(IptcDirectory.TagByLine));
            AddText(values, "IPTC.BylineTitle", iptc.GetString(IptcDirectory.TagByLineTitle));
            AddText(values, "IPTC.Credits", iptc.GetString(IptcDirectory.TagCredit));
            AddText(values, "IPTC.ObjectName", iptc.GetString(IptcDirectory.TagObjectName));
            AddText(values, "IPTC.City", iptc.GetString(IptcDirectory.TagCity));
            AddText(values, "IPTC.State", iptc.GetString(IptcDirectory.TagProvinceOrState));
            AddText(values, "IPTC.Country", iptc.GetString(IptcDirectory.TagCountryOrPrimaryLocationName));
            AddText(values, "IPTC.OriginalTransmissionReference", iptc.GetString(IptcDirectory.TagOriginalTransmissionReference));
            AddText(values, "IPTC.CopyrightNotice", iptc.GetString(IptcDirectory.TagCopyrightNotice));

            if (iptc.TryGetDateTime(IptcDirectory.TagDateCreated, out var created))
                values.TryAdd("IPTC.DateCreated", new TagValue(created.ToString("yyyy-MM-dd"), Date: created));
        }
    }
}

public enum MissingTagPolicy
{
    /// <summary>Leave the file alone and say which tag is missing.</summary>
    Skip,

    /// <summary>Use an empty text for the missing tag.</summary>
    UseEmpty,
}

public sealed class TagRenameOptions
{
    /// <summary>
    /// The new name, with tags in angle brackets and the usual variables: <c>&lt;ID3.Artist&gt; - &lt;ID3.Track&gt;.\e</c>,
    /// <c>&lt;EXIF.OriginalTime:yyyyMMdd_HHmmss&gt;_??.\e</c>. Everything works that works in the normal replace box.
    /// </summary>
    public string Template { get; set; } = @"<ID3.Artist> - <ID3.Track>.\e";

    public MissingTagPolicy Missing { get; set; } = MissingTagPolicy.Skip;

    public int NumberStart { get; set; } = 1;

    public int NumberStep { get; set; } = 1;

    public bool ResetNumberPerFolder { get; set; }

    /// <summary>Restart the counter for every calendar day of the file date (<see cref="DateSource"/>).</summary>
    public bool ResetNumberPerDate { get; set; }

    /// <summary>Tag name such as <c>ID3.Album</c>: items with the same value are counted together.</summary>
    public string? ResetNumberPerTag { get; set; }

    public NumberFormat NumberFormat { get; set; } = NumberFormat.Decimal;

    public TransferKind Kind { get; set; } = TransferKind.Rename;

    public string? TargetFolder { get; set; }

    /// <summary>Shorten the new name to the DOS 8.3 shape (see <see cref="RenameOptions.ShortNames83"/>).</summary>
    public bool ShortNames83 { get; set; }

    public DateSource DateSource { get; set; } = DateSource.Modified;

    /// <summary>The expanded template is a script expression whose value becomes the new name.</summary>
    public bool UseScript { get; set; }

    /// <summary>Evaluates the expressions when <see cref="UseScript"/> is on; null = the default script folder.</summary>
    public ScriptEngine? Script { get; set; }

    public System.Globalization.CultureInfo Culture { get; set; } = System.Globalization.CultureInfo.InvariantCulture;
}

/// <summary>Works out new names from the tags inside the files, without touching the disk.</summary>
public static class TagRenamePlanner
{
    public static IReadOnlyList<RenameItem> Build(
        IReadOnlyList<FileEntry> entries, TagRenameOptions options, ITagReader? reader = null, PlanOptions? plan = null)
    {
        reader ??= FileTagReader.Default;
        plan ??= new PlanOptions();

        var numbering = new RenameOptions
        {
            NumberStart = options.NumberStart,
            NumberStep = options.NumberStep,
            ResetNumberPerFolder = options.ResetNumberPerFolder,
            ResetNumberPerDate = options.ResetNumberPerDate,
            ResetNumberPerTag = options.ResetNumberPerTag,
            TagReader = reader,
            NumberFormat = options.NumberFormat,
            DateSource = options.DateSource,
            Culture = options.Culture,
        };
        var numbers = new NumberSequence(numbering);
        var items = new List<RenameItem>(entries.Count);
        var shortNames = options.ShortNames83 ? new ShortNameRegistry() : null;
        var scriptEngine = options.UseScript ? options.Script ??= new ScriptEngine() : null;
        var scripted = new List<(int Slot, FileEntry Entry, string Expression)>();

        foreach (var entry in entries)
        {
            int number = numbers.Next(entry);
            if (entry.IsDirectory && options.Kind == TransferKind.Rename)
            {
                items.Add(new RenameItem(entry, entry.Name, RenameStatus.Unchanged));
                continue;
            }

            var tags = reader.Read(entry);
            var missing = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            var context = new ExpansionContext(entry, [], number, options.DateSource, options.Culture, tags.Values, missing, options.NumberFormat);
            string newName = options.UseScript
                ? ScriptTemplate.Build(options.Template, scriptEngine!.Language, context, tags: true)
                : ReplacementTemplate.Expand(options.Template, context);

            if (missing.Count > 0 && options.Missing == MissingTagPolicy.Skip)
            {
                string message = "Etiket yok: " + string.Join(", ", missing) + (tags.Error is null ? "" : $" ({tags.Error})");
                items.Add(new RenameItem(entry, entry.Name, RenameStatus.MissingTag, message));
            }
            else if (options.UseScript)
            {
                // Script mode: the name is the value of the expression; evaluated for all items together below.
                scripted.Add((items.Count, entry, newName));
                items.Add(new RenameItem(entry, entry.Name, RenameStatus.Unchanged));
            }
            else
                items.Add(RenamePlanner.MakeItem(entry, newName, options.Kind, options.TargetFolder, shortNames));
        }

        if (scripted.Count > 0)
        {
            var results = scriptEngine!.Evaluate(scripted.Select(s => s.Expression).ToList());
            for (int i = 0; i < scripted.Count; i++)
            {
                var (slot, entry, _) = scripted[i];
                items[slot] = results[i].Error is { } error
                    ? new RenameItem(entry, entry.Name, RenameStatus.Error, error)
                    : RenamePlanner.MakeItem(entry, results[i].Value, options.Kind, options.TargetFolder, shortNames);
            }
        }

        RenamePlanner.ResolveConflicts(items, numbering, plan);
        return items;
    }
}
