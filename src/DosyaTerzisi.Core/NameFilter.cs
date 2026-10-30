using System.Text;
using System.Text.RegularExpressions;

namespace DosyaTerzisi.Core;

/// <summary>
/// File name filter such as <c>*.jpg;*.png</c>: wildcard patterns separated by semicolons, case-insensitive.
/// An empty filter, <c>*</c> and <c>*.*</c> match everything.
/// </summary>
public sealed class NameFilter
{
    public static readonly NameFilter All = new([]);

    private readonly Regex[] _patterns;

    private NameFilter(Regex[] patterns) => _patterns = patterns;

    public static NameFilter Parse(string? filter)
    {
        var patterns = new List<Regex>();

        foreach (var raw in (filter ?? "").Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (raw is "*" or "*.*") return All;
            patterns.Add(new Regex(ToRegex(raw), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline));
        }

        return patterns.Count == 0 ? All : new NameFilter([.. patterns]);
    }

    public bool IsMatch(string name) => _patterns.Length == 0 || _patterns.Any(p => p.IsMatch(name));

    private static string ToRegex(string pattern)
    {
        var sb = new StringBuilder("^");
        foreach (char c in pattern)
        {
            sb.Append(c switch
            {
                '*' => ".*",
                '?' => ".",
                _ => Regex.Escape(c.ToString()),
            });
        }
        return sb.Append('$').ToString();
    }
}
