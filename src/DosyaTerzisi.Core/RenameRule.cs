using System.Text;
using System.Text.RegularExpressions;

namespace DosyaTerzisi.Core;

public sealed class RenameRuleException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>A compiled search/replace rule. Create once, apply to many entries.</summary>
public sealed class RenameRule
{
    private readonly RenameOptions _options;
    private readonly Regex? _regex;
    private readonly CharacterTranslation? _translation;
    private readonly string _replace;

    private RenameRule(RenameOptions options, Regex? regex, CharacterTranslation? translation, string replace)
    {
        _options = options;
        _regex = regex;
        _translation = translation;
        _replace = replace;
    }

    public static RenameRule Create(RenameOptions options)
    {
        var regexOptions = RegexOptions.CultureInvariant | RegexOptions.Singleline;
        if (!options.CaseSensitive) regexOptions |= RegexOptions.IgnoreCase;

        try
        {
            return options.Method switch
            {
                RenameMethod.Wildcard => new RenameRule(
                    options, new Regex(WildcardToRegex(options.Search), regexOptions), null, options.Replace),

                // %%text%% marks text meant for translation; the text is kept as is.
                RenameMethod.RegularExpression => new RenameRule(
                    options, new Regex(StripMarkers(options.Search), regexOptions), null, StripMarkers(options.Replace)),

                RenameMethod.Translation => new RenameRule(
                    options, null, new CharacterTranslation(options.Search, options.Replace), options.Replace),

                _ => throw new RenameRuleException($"Unknown rename method: {options.Method}"),
            };
        }
        catch (ArgumentException ex)
        {
            throw new RenameRuleException(ex.Message, ex);
        }
    }

    /// <param name="number">Value of the <c>?</c> counter for this entry.</param>
    public string Apply(FileEntry entry, int number)
    {
        if (_translation is not null)
        {
            return _translation.Apply(entry.Title) + entry.Name[entry.Title.Length..];
        }

        if (_options.Method == RenameMethod.Wildcard)
        {
            var match = _regex!.Match(entry.Name);
            return match.Success ? Expand(entry, match, number) : entry.Name;
        }

        return _regex!.Replace(entry.Name, match => Expand(entry, match, number));
    }

    /// <summary>True when the replacement is a script expression (see <see cref="PlanScript"/>).</summary>
    public bool UsesScript => _options.UseScript;

    internal ScriptEngine ScriptEngine => _options.Script ??= new ScriptEngine();

    /// <summary>
    /// Script mode, first pass: builds the expression(s) for one entry. The planner evaluates the expressions of all
    /// entries in one go and then calls <see cref="ScriptJob.Finish"/> to get the new name.
    /// </summary>
    internal ScriptJob PlanScript(FileEntry entry, int number)
    {
        string language = ScriptEngine.Language;
        if (_translation is not null)
            return new ScriptJob([], _ => (Apply(entry, number), null));

        string Build(Match match)
        {
            var groups = new string[Math.Max(match.Groups.Count, 10)];
            for (int i = 0; i < groups.Length; i++)
                groups[i] = i < match.Groups.Count ? match.Groups[i].Value : "";
            return ScriptTemplate.Build(
                _replace, language,
                new ExpansionContext(entry, groups, number, _options.DateSource, _options.Culture, NumberFormat: _options.NumberFormat));
        }

        if (_options.Method == RenameMethod.Wildcard)
        {
            var match = _regex!.Match(entry.Name);
            if (!match.Success) return new ScriptJob([], _ => (entry.Name, null));
            return new ScriptJob([Build(match)], r => r[0].Error is { } e ? (entry.Name, e) : (r[0].Value, null));
        }

        var matches = _regex!.Matches(entry.Name).ToList();
        var expressions = matches.Select(Build).ToList();
        return new ScriptJob(expressions, results =>
        {
            if (results.FirstOrDefault(r => r.Error is not null).Error is { } error) return (entry.Name, error);

            var sb = new StringBuilder();
            int last = 0;
            for (int i = 0; i < matches.Count; i++)
            {
                sb.Append(entry.Name, last, matches[i].Index - last).Append(results[i].Value);
                last = matches[i].Index + matches[i].Length;
            }
            return (sb.Append(entry.Name, last, entry.Name.Length - last).ToString(), null);
        });
    }

    private string Expand(FileEntry entry, Match match, int number)
    {
        var groups = new string[Math.Max(match.Groups.Count, 10)];
        for (int i = 0; i < groups.Length; i++)
            groups[i] = i < match.Groups.Count ? match.Groups[i].Value : "";

        return ReplacementTemplate.Expand(
            _replace,
            new ExpansionContext(entry, groups, number, _options.DateSource, _options.Culture, NumberFormat: _options.NumberFormat));
    }

    private static string WildcardToRegex(string pattern)
    {
        var sb = new StringBuilder("^");
        foreach (char c in pattern)
        {
            sb.Append(c switch
            {
                '*' => "(.*)",
                '?' => "(.)",
                _ => Regex.Escape(c.ToString()),
            });
        }
        return sb.Append('$').ToString();
    }

    private static string StripMarkers(string text) => text.Replace("%%", "");
}
