using System.Diagnostics.CodeAnalysis;

namespace Erp.Core.Common;

/// <summary>
/// A parsed document number like <c>SO/2026-27/000042</c>.
/// <c>readonly record struct</c>: a small immutable VALUE type with value
/// equality, copied rather than referenced, and no heap allocation.
/// </summary>
public readonly record struct DocumentNumber(string Prefix, string FinancialYear, int Sequence)
{
    public override string ToString() => $"{Prefix}/{FinancialYear}/{Sequence:000000}";

    /// <summary>
    /// Parses without allocating substrings: <see cref="ReadOnlySpan{T}"/> is a
    /// view over the original characters (slicing = no copy). The <c>out</c>
    /// parameter must be assigned inside the method; compare <c>ref</c>, which
    /// the caller must initialise first.
    /// </summary>
    public static bool TryParse(ReadOnlySpan<char> text, [NotNullWhen(true)] out DocumentNumber? result)
    {
        result = null;
        int first = text.IndexOf('/');
        if (first <= 0) return false;
        ReadOnlySpan<char> prefix = text[..first];
        ReadOnlySpan<char> rest = text[(first + 1)..];

        int second = rest.IndexOf('/');
        if (second <= 0) return false;
        ReadOnlySpan<char> fy = rest[..second];
        ReadOnlySpan<char> seq = rest[(second + 1)..];

        // financial year: 4 digits, '-', 2 digits, and the 2nd year follows the 1st (2026-27)
        if (fy.Length != 7 || fy[4] != '-' ||
            !int.TryParse(fy[..4], out int startYear) || !int.TryParse(fy[5..], out int endYY) ||
            (startYear + 1) % 100 != endYY)
            return false;

        if (seq.Length != 6 || !int.TryParse(seq, out int sequence) || sequence <= 0) return false;
        foreach (char c in prefix)
            if (!char.IsAsciiLetterUpper(c)) return false;

        result = new DocumentNumber(prefix.ToString(), fy.ToString(), sequence);   // allocate only on success
        return true;
    }
}
