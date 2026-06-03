using System.Globalization;
using System.Text;

namespace Bistable.App.Services;

/// <summary>
/// Parses memory-image text files (Verilog <c>$readmemh</c> / <c>$readmemb</c>
/// style) into a sparse address → value map ready to feed into
/// <see cref="LiveProbeService.WriteMemoryCellAsync"/>.
///
/// Supported syntax:
/// <list type="bullet">
///   <item><description>One value per whitespace-separated token; hex tokens may include underscores (<c>1234_5678</c>).</description></item>
///   <item><description><c>@&lt;hex&gt;</c> sets the current write address — anything after lands at that address (and increments).</description></item>
///   <item><description><c>//</c> line comments, <c>/* … */</c> block comments, <c>#</c> line comments.</description></item>
///   <item><description>Binary mode parses bits and packs them as nibbles into the same cellWidth/4 hex output.</description></item>
/// </list>
/// </summary>
public static class MemoryFileLoader
{
    public sealed record MemoryImage(IReadOnlyList<MemoryImageCell> Cells, int Lines, int Errors)
    {
        public int CellCount => Cells.Count;
    }

    public sealed record MemoryImageCell(ulong Address, string HexValue);

    public enum NumeralBase { Hex, Bin }

    /// <summary>
    /// Parses the file at <paramref name="path"/>. <paramref name="cellWidth"/>
    /// is the destination memory's per-cell width in bits — used to format the
    /// resulting hex strings with the right zero-padding and to range-check
    /// each value. <paramref name="depth"/> filters out-of-range addresses.
    /// </summary>
    public static MemoryImage LoadFromFile(string path, int cellWidth, int depth, NumeralBase format = NumeralBase.Hex)
    {
        string text = File.ReadAllText(path);
        return Parse(text, cellWidth, depth, format);
    }

    public static MemoryImage Parse(string text, int cellWidth, int depth, NumeralBase format = NumeralBase.Hex)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (cellWidth <= 0) throw new ArgumentOutOfRangeException(nameof(cellWidth));

        List<MemoryImageCell> cells = new();
        ParseState state = new(cursor: 0, lines: 0, errors: 0);
        int hexDigits = Math.Max(1, (cellWidth + 3) / 4);
        ulong maxValue = cellWidth >= 64 ? ulong.MaxValue : (1UL << cellWidth) - 1;

        foreach (string rawToken in EnumerateTokens(StripComments(text)))
        {
            string token = rawToken.Trim();
            if (token.Length == 0) continue;
            state.Lines++;
            ProcessToken(token, format, depth, maxValue, hexDigits, cells, ref state);
        }

        return new MemoryImage(cells, state.Lines, state.Errors);
    }

    private struct ParseState
    {
        public ulong Cursor;
        public int Lines;
        public int Errors;
        public ParseState(ulong cursor, int lines, int errors) { Cursor = cursor; Lines = lines; Errors = errors; }
    }

    private static void ProcessToken(
        string token, NumeralBase format, int depth, ulong maxValue, int hexDigits,
        List<MemoryImageCell> cells, ref ParseState state)
    {
        if (token.StartsWith('@'))
        {
            string addrText = token.AsSpan(1).ToString().Replace("_", "");
            if (ulong.TryParse(addrText, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong addr))
            {
                state.Cursor = addr;
            }
            else
            {
                state.Errors++;
            }
            return;
        }

        string compact = token.Replace("_", "");
        if (!TryParseValue(compact, format, out ulong value) || value > maxValue)
        {
            state.Errors++;
            return;
        }
        if (depth > 0 && state.Cursor >= (ulong)depth)
        {
            state.Errors++;
            state.Cursor++;
            return;
        }

        // Emit with the "0x" prefix so the worker's parse_u64 routes through
        // base-16 instead of treating "00500093" as a decimal literal (which
        // would silently corrupt every loaded program — 0x500093 became 500093
        // decimal = 0x7A17D in the first wave of testing).
        cells.Add(new MemoryImageCell(state.Cursor, "0x" + value.ToString("x" + hexDigits, CultureInfo.InvariantCulture)));
        state.Cursor++;
    }

    private static bool TryParseValue(string token, NumeralBase format, out ulong value)
    {
        switch (format)
        {
            case NumeralBase.Hex:
                return ulong.TryParse(token, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
            case NumeralBase.Bin:
                value = 0;
                if (token.Length == 0 || token.Length > 64) return false;
                foreach (char c in token)
                {
                    if (c != '0' && c != '1') return false;
                    value = (value << 1) | (uint)(c - '0');
                }
                return true;
            default:
                value = 0;
                return false;
        }
    }

    // Strips // line comments, # line comments, and /* block */ comments.
    // Preserves token boundaries (newlines / spaces) so neighbouring values
    // don't accidentally merge.
    private static string StripComments(string text)
    {
        StringBuilder sb = new(text.Length);
        int i = 0;
        while (i < text.Length)
        {
            int skip = TryConsumeComment(text, i, sb);
            if (skip > 0) { i = skip; continue; }
            sb.Append(text[i]);
            i++;
        }
        return sb.ToString();
    }

    // Returns the new index past the comment, or 0 when no comment starts at `i`.
    private static int TryConsumeComment(string text, int i, StringBuilder sb)
    {
        char c = text[i];
        if (c == '/' && i + 1 < text.Length && text[i + 1] == '/')
        {
            int eol = text.IndexOf('\n', i);
            return eol < 0 ? text.Length : eol;
        }
        if (c == '/' && i + 1 < text.Length && text[i + 1] == '*')
        {
            int end = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
            sb.Append(' '); // keep a space so tokens around the block stay separated
            return end < 0 ? text.Length : end + 2;
        }
        if (c == '#')
        {
            int eol = text.IndexOf('\n', i);
            return eol < 0 ? text.Length : eol;
        }
        return 0;
    }

    private static IEnumerable<string> EnumerateTokens(string text)
    {
        int i = 0;
        while (i < text.Length)
        {
            while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
            if (i >= text.Length) yield break;
            int start = i;
            while (i < text.Length && !char.IsWhiteSpace(text[i])) i++;
            yield return text[start..i];
        }
    }
}
