using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;

namespace Erp.Core.Common;

/// <summary>Builds CSV text lazily, one line at a time.</summary>
public static class CsvExporter
{
    /// <summary>
    /// ITERATOR with <c>yield return</c>: nothing runs until the caller
    /// enumerates, and only one line exists in memory at a time, so exporting
    /// 1 million rows doesn't build a 1-million-line list first.
    /// The column selectors are <c>Func&lt;T, object?&gt;</c> delegates.
    /// </summary>
    public static IEnumerable<string> ToCsvLines<T>(
        IEnumerable<T> rows, IReadOnlyList<(string Header, Func<T, object?> Value)> columns)
    {
        yield return HeaderLine(columns);
        var sb = new StringBuilder();
        foreach (T row in rows)
            yield return FormatRow(sb, row, columns);
    }

    /// <summary>
    /// ASYNC ITERATOR: <c>await foreach</c> over a database stream
    /// (IAsyncEnumerable) and <c>yield return</c> each line. The API writes each
    /// line to the response as soon as it's produced.
    /// </summary>
    public static async IAsyncEnumerable<string> ToCsvLinesAsync<T>(
        IAsyncEnumerable<T> rows,
        IReadOnlyList<(string Header, Func<T, object?> Value)> columns,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        yield return HeaderLine(columns);
        var sb = new StringBuilder();
        await foreach (T row in rows.WithCancellation(ct))
            yield return FormatRow(sb, row, columns);
    }

    private static string HeaderLine<T>(IReadOnlyList<(string Header, Func<T, object?> Value)> columns) =>
        string.Join(',', columns.Select(c => Escape(c.Header)));

    // StringBuilder: one mutable buffer reused per row. 'line += value' in a loop
    // would allocate a new string on every concatenation (strings are immutable).
    private static string FormatRow<T>(StringBuilder sb, T row, IReadOnlyList<(string Header, Func<T, object?> Value)> columns)
    {
        sb.Clear();
        for (int i = 0; i < columns.Count; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(Escape(Format(columns[i].Value(row))));
        }
        return sb.ToString();
    }

    private static string Format(object? value) => value switch
    {
        null => "",
        DateTime d => d.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),   // decimals: always '.' separator
        _ => value.ToString() ?? ""
    };

    /// <summary>RFC 4180: quote fields containing comma, quote or newline; double the quotes.</summary>
    private static string Escape(string field) =>
        field.AsSpan().IndexOfAny(",\"\r\n") >= 0 ? $"\"{field.Replace("\"", "\"\"")}\"" : field;
}
