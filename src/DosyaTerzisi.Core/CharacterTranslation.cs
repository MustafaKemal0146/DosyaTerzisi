namespace DosyaTerzisi.Core;

/// <summary>
/// Maps every character of the search list to the character at the same position of the replace list.
/// Ranges such as <c>a-z</c> are expanded first. Characters past the end of the replace list stay unchanged.
/// </summary>
public sealed class CharacterTranslation
{
    private readonly Dictionary<char, char> _map = new();

    public CharacterTranslation(string search, string replace)
    {
        var from = ExpandRanges(search);
        var to = ExpandRanges(replace);

        int count = Math.Min(from.Count, to.Count);
        for (int i = 0; i < count; i++)
            _map.TryAdd(from[i], to[i]);
    }

    public string Apply(string text)
    {
        if (_map.Count == 0) return text;

        return string.Create(text.Length, text, (span, source) =>
        {
            for (int i = 0; i < source.Length; i++)
                span[i] = _map.TryGetValue(source[i], out var mapped) ? mapped : source[i];
        });
    }

    internal static List<char> ExpandRanges(string list)
    {
        var result = new List<char>();
        for (int i = 0; i < list.Length; i++)
        {
            bool isRange = i + 2 < list.Length && list[i + 1] == '-' && list[i + 2] >= list[i];
            if (isRange)
            {
                for (char c = list[i]; c <= list[i + 2]; c++)
                {
                    result.Add(c);
                    if (c == char.MaxValue) break;
                }
                i += 2;
            }
            else
            {
                result.Add(list[i]);
            }
        }
        return result;
    }
}
