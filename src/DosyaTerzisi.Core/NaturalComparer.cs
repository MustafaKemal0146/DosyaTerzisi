using System.Globalization;

namespace DosyaTerzisi.Core;

/// <summary>Sorts names the way Explorer does: "file2" before "file10", letters compared without case.</summary>
public sealed class NaturalComparer(CultureInfo culture) : IComparer<string>
{
    public static readonly NaturalComparer Invariant = new(CultureInfo.InvariantCulture);

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return -1;
        if (y is null) return 1;

        int i = 0, j = 0;
        while (i < x.Length && j < y.Length)
        {
            if (char.IsAsciiDigit(x[i]) && char.IsAsciiDigit(y[j]))
            {
                int result = CompareNumbers(x, ref i, y, ref j);
                if (result != 0) return result;
                continue;
            }

            int xs = i, ys = j;
            while (i < x.Length && !char.IsAsciiDigit(x[i])) i++;
            while (j < y.Length && !char.IsAsciiDigit(y[j])) j++;

            int text = string.Compare(x[xs..i], y[ys..j], culture, CompareOptions.IgnoreCase);
            if (text != 0) return text;
        }

        return (x.Length - i).CompareTo(y.Length - j);
    }

    private static int CompareNumbers(string x, ref int i, string y, ref int j)
    {
        int xs = i, ys = j;
        while (i < x.Length && char.IsAsciiDigit(x[i])) i++;
        while (j < y.Length && char.IsAsciiDigit(y[j])) j++;

        var a = x.AsSpan(xs, i - xs).TrimStart('0');
        var b = y.AsSpan(ys, j - ys).TrimStart('0');

        return a.Length != b.Length ? a.Length.CompareTo(b.Length) : a.SequenceCompareTo(b);
    }
}
