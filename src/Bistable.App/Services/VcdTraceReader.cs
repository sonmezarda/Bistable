using System.Globalization;
using System.Numerics;

namespace Bistable.App.Services;

public sealed class VcdTraceReader
{
    public VcdTraceDocument Load(string path, string topModuleName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(topModuleName);

        if (!File.Exists(path))
        {
            return VcdTraceDocument.Empty;
        }

        List<string> scopeStack = [];
        Dictionary<string, List<VcdTraceSignal>> signalsByCode = new(StringComparer.Ordinal);
        Dictionary<string, List<VcdTraceEvent>> eventsBySignal = new(StringComparer.OrdinalIgnoreCase);
        ulong currentTime = 0;
        ulong maxTime = 0;
        long order = 0;

        using StreamReader reader = new(
            new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
        foreach (string rawLine in reader.ReadToEnd().Split('\n'))
        {
            string line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (line[0] == '$')
            {
                HandleDirective(line, topModuleName, scopeStack, signalsByCode, eventsBySignal);
                continue;
            }

            if (line[0] == '#')
            {
                if (ulong.TryParse(line[1..], NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong parsedTime))
                {
                    currentTime = parsedTime;
                    maxTime = Math.Max(maxTime, currentTime);
                }

                continue;
            }

            if (!TryParseValueChange(line, signalsByCode, currentTime, ref order, out IReadOnlyList<VcdTraceEvent>? traceEvents))
            {
                continue;
            }

            foreach (VcdTraceEvent parsedEvent in traceEvents!)
            {
                if (!eventsBySignal.TryGetValue(parsedEvent.SignalName, out List<VcdTraceEvent>? signalEvents))
                {
                    signalEvents = [];
                    eventsBySignal[parsedEvent.SignalName] = signalEvents;
                }

                signalEvents.Add(parsedEvent);
                maxTime = Math.Max(maxTime, parsedEvent.Time);
            }
        }

        Dictionary<string, IReadOnlyList<VcdTraceEvent>> readOnlyEvents = eventsBySignal.ToDictionary(
            static pair => pair.Key,
            static pair => (IReadOnlyList<VcdTraceEvent>)pair.Value,
            StringComparer.OrdinalIgnoreCase);

        IReadOnlyList<VcdTraceSignal> signals = signalsByCode.Values
            .SelectMany(static signalGroup => signalGroup)
            .DistinctBy(static signal => signal.Name, StringComparer.OrdinalIgnoreCase)
            .OrderBy(static signal => signal.IsTopLevel ? 0 : 1)
            .ThenBy(static signal => signal.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new VcdTraceDocument(signals, readOnlyEvents, order, maxTime);
    }

    private static void HandleDirective(
        string line,
        string topModuleName,
        List<string> scopeStack,
        IDictionary<string, List<VcdTraceSignal>> signalsByCode,
        IDictionary<string, List<VcdTraceEvent>> eventsBySignal)
    {
        string[] tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
        {
            return;
        }

        if (tokens[0] == "$scope" && tokens.Length >= 3)
        {
            scopeStack.Add(tokens[2]);
            return;
        }

        if (tokens[0] == "$upscope")
        {
            if (scopeStack.Count > 0)
            {
                scopeStack.RemoveAt(scopeStack.Count - 1);
            }

            return;
        }

        if (tokens[0] != "$var" || tokens.Length < 5)
        {
            return;
        }

        if (!int.TryParse(tokens[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int width))
        {
            width = 1;
        }

        string id = tokens[3];
        string shortName = tokens[4];
        string scopePath = NormalizeScope(string.Join('.', scopeStack));
        bool isTopLevel = string.Equals(scopePath, topModuleName, StringComparison.OrdinalIgnoreCase);
        string logicalName = isTopLevel || string.IsNullOrWhiteSpace(scopePath)
            ? shortName
            : $"{scopePath}.{shortName}";

        VcdTraceSignal signal = new(logicalName, shortName, scopePath, Math.Max(1, width), isTopLevel);
        if (!signalsByCode.TryGetValue(id, out List<VcdTraceSignal>? aliases))
        {
            aliases = [];
            signalsByCode[id] = aliases;
        }

        aliases.Add(signal);
        if (!eventsBySignal.ContainsKey(logicalName))
        {
            eventsBySignal[logicalName] = [];
        }
    }

    private static bool TryParseValueChange(
        string line,
        IReadOnlyDictionary<string, List<VcdTraceSignal>> signalsByCode,
        ulong currentTime,
        ref long order,
        out IReadOnlyList<VcdTraceEvent>? traceEvents)
    {
        traceEvents = null;

        if (line[0] is 'b' or 'B')
        {
            int separatorIndex = line.IndexOf(' ');
            if (separatorIndex <= 1)
            {
                return false;
            }

            string bits = line[1..separatorIndex];
            string id = line[(separatorIndex + 1)..].Trim();
            if (!signalsByCode.TryGetValue(id, out List<VcdTraceSignal>? signals) || signals.Count == 0)
            {
                return false;
            }

            string value = FormatVectorValue(bits, signals[0].Width);
            List<VcdTraceEvent> events = [];
            foreach (VcdTraceSignal signal in signals)
            {
                events.Add(new VcdTraceEvent(++order, currentTime, signal.Name, value));
            }

            traceEvents = events;
            return true;
        }

        char prefix = line[0];
        if (prefix is not ('0' or '1' or 'x' or 'X' or 'z' or 'Z'))
        {
            return false;
        }

        string scalarId = line[1..].Trim();
        if (!signalsByCode.TryGetValue(scalarId, out List<VcdTraceSignal>? scalarSignals) || scalarSignals.Count == 0)
        {
            return false;
        }

        string scalarValue = char.ToLowerInvariant(prefix).ToString(CultureInfo.InvariantCulture);
        List<VcdTraceEvent> scalarEvents = [];
        foreach (VcdTraceSignal signal in scalarSignals)
        {
            scalarEvents.Add(new VcdTraceEvent(++order, currentTime, signal.Name, scalarValue));
        }

        traceEvents = scalarEvents;
        return true;
    }

    private static string NormalizeScope(string scopePath)
    {
        string normalized = scopePath;
        while (normalized.StartsWith("TOP.", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[4..];
        }

        if (string.Equals(normalized, "TOP", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        return normalized;
    }

    private static string FormatVectorValue(string bits, int width)
    {
        string normalized = bits.Trim().Replace("_", string.Empty, StringComparison.Ordinal);
        if (normalized.Length == 0)
        {
            return width == 1 ? "0" : "0x0";
        }

        if (normalized.Any(static bit => bit is 'x' or 'X' or 'z' or 'Z'))
        {
            return "0b" + normalized.ToLowerInvariant();
        }

        BigInteger value = BigInteger.Zero;
        foreach (char bit in normalized)
        {
            value <<= 1;
            if (bit == '1')
            {
                value += BigInteger.One;
            }
        }

        int digits = Math.Max(1, (Math.Max(width, normalized.Length) + 3) / 4);
        return "0x" + value.ToString("X", CultureInfo.InvariantCulture).PadLeft(digits, '0');
    }
}
