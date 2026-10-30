using System.Globalization;
using System.Text;

namespace DosyaTerzisi.Core;

/// <summary>Values a replacement template can read.</summary>
/// <param name="Tags">Values for <c>&lt;ID3.Artist&gt;</c> style tags. Null = <c>&lt;</c> is an ordinary character.</param>
/// <param name="MissingTags">Collects the names of tags that were asked for but not found.</param>
public sealed record ExpansionContext(
    FileEntry Entry,
    IReadOnlyList<string> Groups,
    int Number,
    DateSource DateSource,
    CultureInfo Culture,
    IReadOnlyDictionary<string, TagValue>? Tags = null,
    ISet<string>? MissingTags = null,
    NumberFormat NumberFormat = NumberFormat.Decimal);

/// <summary>How the <c>?</c> counter is written.</summary>
public enum NumberFormat
{
    /// <summary><c>1, 2, 10</c>; with <c>??</c>: <c>01, 02, 10</c>.</summary>
    Decimal,

    /// <summary><c>1, 9, A, 1F</c>; with <c>??</c>: <c>01, 09, 0A, 1F</c>.</summary>
    Hexadecimal,

    /// <summary>
    /// Letters as digits of a base-26 number, a = 0: <c>a, b, ... z, ba, bb</c>; with <c>??</c>: <c>aa, ab, ... az, ba</c>.
    /// Use the capital-letter variable for capitals.
    /// </summary>
    Letters,
}

public static class NumberFormatting
{
    public static string Format(long number, int width, NumberFormat format)
    {
        switch (format)
        {
            case NumberFormat.Hexadecimal:
                return number.ToString("X" + width, CultureInfo.InvariantCulture);

            case NumberFormat.Letters:
                if (number < 0) return "-" + Format(-number, width, format);
                var letters = new StringBuilder();
                do
                {
                    letters.Insert(0, (char)('a' + number % 26));
                    number /= 26;
                } while (number > 0);
                return letters.ToString().PadLeft(width, 'a');

            default:
                return number.ToString("D" + width, CultureInfo.InvariantCulture);
        }
    }
}

/// <summary>
/// Expands a replacement string such as <c>??_\1.\e</c>.
/// <c>?</c> run = zero padded counter, <c>\0 \t \e \f \F</c> = name parts, <c>\1</c>..<c>\9</c> = captured groups,
/// <c>\Y \y \m \d \H \M \S \p \I  \A  \B</c> = file date and time (weekday and month names),
/// <c>\; \:</c> = file size with / without thousand separators, <c>\P</c> = full folder path, <c>\u \U \l \L \E</c> = case changes.
/// <c>%Y %m %d %H %M %S ...</c> (strftime style) is the same as the backslash form; <c>%%</c> is one percent sign,
/// <c>%#d</c> is without the leading zero, <c>%</c> before any other character stays as it is.
/// Any other <c>\x</c> is the literal character x.
/// </summary>
public static class ReplacementTemplate
{
    public static string Expand(string template, ExpansionContext ctx)
    {
        var writer = new CaseWriter(ctx.Culture);
        var date = ctx.DateSource == DateSource.Created ? ctx.Entry.Created : ctx.Entry.Modified;

        for (int i = 0; i < template.Length; i++)
        {
            char c = template[i];

            if (c == '?')
            {
                int end = i;
                while (end < template.Length && template[end] == '?') end++;
                int width = end - i;
                writer.Append(NumberFormatting.Format(ctx.Number, width, ctx.NumberFormat));
                i = end - 1;
                continue;
            }

            if (c == '*')
            {
                int end = i;
                while (end < template.Length && template[end] == '*') end++;
                writer.Append(RandomDigits(ctx.Entry, i, Math.Min(end - i, 9)));
                i = end - 1;
                continue;
            }

            if (c == '<' && ctx.Tags is not null && TryReadTag(template, i, out int tagEnd, out string tagName, out string? format))
            {
                writer.Append(TagText(ctx, tagName, format));
                i = tagEnd;
                continue;
            }

            if (c == '%' && i + 1 < template.Length)
            {
                // strftime style: "%Y-%m-%d" is the same as "\Y-\m-\d"; "%%" is one percent sign; "%#d" has no leading zero.
                char next = template[i + 1];
                if (next == '%')
                {
                    writer.Append('%');
                    i++;
                    continue;
                }
                if (IsDateCode(next))
                {
                    writer.Append(DatePart(next, date, ctx.Culture));
                    i++;
                    continue;
                }
                if (next == '#' && i + 2 < template.Length && IsDateCode(template[i + 2]))
                {
                    writer.Append(TrimZeros(DatePart(template[i + 2], date, ctx.Culture)));
                    i += 2;
                    continue;
                }
                writer.Append(c);
                continue;
            }

            if (c != '\\' || i + 1 >= template.Length)
            {
                writer.Append(c);
                continue;
            }

            char code = template[++i];
            switch (code)
            {
                case >= '1' and <= '9':
                    int group = code - '0';
                    if (group < ctx.Groups.Count) writer.Append(ctx.Groups[group]);
                    break;
                case '0': writer.Append(ctx.Entry.Name); break;
                case 't': writer.Append(ctx.Entry.Title); break;
                case 'e': writer.Append(ctx.Entry.Extension); break;
                case 'f': writer.Append(ctx.Entry.FolderName); break;
                case 'F': writer.Append(ctx.Entry.ParentFolderName); break;
                case ';': writer.Append(ctx.Entry.Size.ToString("N0", ctx.Culture)); break;
                case ':': writer.Append(ctx.Entry.Size.ToString(CultureInfo.InvariantCulture)); break;
                case 'P': writer.Append(NameValidator.CleanForFileName(ctx.Entry.Directory)); break;
                case 'Y' or 'y' or 'm' or 'd' or 'H' or 'M' or 'S' or 'p' or 'I' or 'a' or 'A' or 'b' or 'B' or 'j' or 'w' or 'x' or 'X' or 'c':
                    writer.Append(DatePart(code, date, ctx.Culture));
                    break;
                case '#' when i + 1 < template.Length && IsDateCode(template[i + 1]):
                    // "\#d": the same as \d without the leading zero (5 instead of 05).
                    writer.Append(TrimZeros(DatePart(template[++i], date, ctx.Culture)));
                    break;
                case 'Q':
                    // Everything up to \E is taken literally: no variables, no case changes.
                    int quoteEnd = template.IndexOf("\\E", i + 1, StringComparison.Ordinal);
                    writer.AppendLiteral(quoteEnd < 0 ? template[(i + 1)..] : template[(i + 1)..quoteEnd]);
                    i = quoteEnd < 0 ? template.Length : quoteEnd + 1;
                    break;
                case 'u': writer.UpperNext(); break;
                case 'l': writer.LowerNext(); break;
                case 'U': writer.UpperUntilEnd(); break;
                case 'L': writer.LowerUntilEnd(); break;
                case 'E': writer.EndCase(); break;
                default: writer.Append(code); break;
            }
        }

        return writer.ToString();
    }

    private static string TrimZeros(string padded)
    {
        string trimmed = padded.TrimStart('0');
        return trimmed.Length == 0 ? "0" : trimmed;
    }

    private static bool IsDateCode(char c) =>"YymdHMSpIaAbBjwxXc".Contains(c);

    private static string DatePart(char code, DateTime date, CultureInfo culture) => code switch
    {
        'Y' => date.ToString("yyyy", CultureInfo.InvariantCulture),
        'y' => date.ToString("yy", CultureInfo.InvariantCulture),
        'm' => date.ToString("MM", CultureInfo.InvariantCulture),
        'd' => date.ToString("dd", CultureInfo.InvariantCulture),
        'H' => date.ToString("HH", CultureInfo.InvariantCulture),
        'M' => date.ToString("mm", CultureInfo.InvariantCulture),
        'S' => date.ToString("ss", CultureInfo.InvariantCulture),
        'p' => date.Hour < 12 ? "AM" : "PM",
        'I' => date.ToString("hh", CultureInfo.InvariantCulture),
        'a' => date.ToString("ddd", culture),
        'A' => date.ToString("dddd", culture),
        'b' => date.ToString("MMM", culture),
        'B' => date.ToString("MMMM", culture),
        'j' => date.DayOfYear.ToString("D3", CultureInfo.InvariantCulture),
        'w' => ((int)date.DayOfWeek).ToString(CultureInfo.InvariantCulture),
        'x' => FileSafe(date.ToString("d", culture)),
        'X' => FileSafe(date.ToString("T", culture)),
        _ => FileSafe(date.ToString("G", culture)),
    };

    /// <summary>Locale dates use "/" and ":", which a file name cannot hold.</summary>
    private static string FileSafe(string text) => NameValidator.CleanForFileName(text.Replace('/', '-').Replace(':', '.'));

    /// <summary>
    /// Digits for a run of <c>*</c>. Derived from the file and the position, so the preview shows exactly the
    /// numbers that will be used, yet every file gets its own.
    /// </summary>
    private static string RandomDigits(FileEntry entry, int position, int width)
    {
        var hash = System.Security.Cryptography.SHA1.HashData(Encoding.UTF8.GetBytes($"{entry.FullPath.ToLowerInvariant()}|{position}"));
        long limit = (long)Math.Pow(10, width);
        long value = (long)(BitConverter.ToUInt64(hash, 0) % (ulong)limit);
        return value.ToString("D" + width, CultureInfo.InvariantCulture);
    }

    /// <summary>Reads <c>&lt;Group.Name&gt;</c> or <c>&lt;Group.Name:format&gt;</c> starting at <paramref name="start"/>.</summary>
    private static bool TryReadTag(string template, int start, out int end, out string name, out string? format)
    {
        end = template.IndexOf('>', start + 1);
        name = "";
        format = null;
        if (end < 0) return false;

        string inner = template[(start + 1)..end];
        int colon = inner.IndexOf(':');
        name = colon < 0 ? inner : inner[..colon];
        format = colon < 0 ? null : inner[(colon + 1)..];

        // Group.Name: letters and digits, one dot, the name may contain a dash (F-Number).
        int dot = name.IndexOf('.');
        return dot > 0
            && dot < name.Length - 1
            && name[..dot].All(char.IsAsciiLetterOrDigit)
            && name[(dot + 1)..].All(ch => char.IsAsciiLetterOrDigit(ch) || ch == '-');
    }

    private static string TagText(ExpansionContext ctx, string name, string? format)
    {
        if (!ctx.Tags!.TryGetValue(name, out var tag))
        {
            ctx.MissingTags?.Add(name);
            return "";
        }

        string text = format is { Length: > 0 }
            ? tag.Date?.ToString(format, CultureInfo.InvariantCulture)
              ?? tag.Number?.ToString(format, CultureInfo.InvariantCulture)
              ?? tag.Text
            : tag.Date?.ToString("yyyy-MM-dd HH.mm.ss", CultureInfo.InvariantCulture) ?? tag.Text;

        return NameValidator.CleanForFileName(text);
    }

    /// <summary>Applies \u \U \l \L \E while text is appended.</summary>
    private sealed class CaseWriter(CultureInfo culture)
    {
        private enum Mode { None, Upper, Lower }

        private readonly StringBuilder _sb = new();
        private Mode _span = Mode.None;
        private Mode _next = Mode.None;

        /// <summary>Text taken as it is: not changed by \u \U \l \L.</summary>
        public void AppendLiteral(string text) => _sb.Append(text);

        public void UpperNext() => _next = Mode.Upper;
        public void LowerNext() => _next = Mode.Lower;
        public void UpperUntilEnd() => _span = Mode.Upper;
        public void LowerUntilEnd() => _span = Mode.Lower;

        public void EndCase()
        {
            _span = Mode.None;
            _next = Mode.None;
        }

        public void Append(char c) => Append(c.ToString());

        public void Append(string text)
        {
            foreach (char c in text)
            {
                var mode = _next != Mode.None ? _next : _span;
                _next = Mode.None;
                _sb.Append(mode switch
                {
                    Mode.Upper => char.ToUpper(c, culture),
                    Mode.Lower => char.ToLower(c, culture),
                    _ => c,
                });
            }
        }

        public override string ToString() => _sb.ToString();
    }
}
