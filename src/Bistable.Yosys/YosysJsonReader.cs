using System.Text.Json;
using Bistable.Core.Synthesis;

namespace Bistable.Yosys;

/// <summary>
/// Phase 6 P6-4: parses Yosys's <c>write_json</c> output into a
/// <see cref="GateNetlist"/>. Reference format:
/// <see href="https://yosyshq.readthedocs.io/projects/yosys/en/latest/cmd/write_json.html"/>.
///
/// Spec quirks we handle:
/// - Bit values may be integers (net ids ≥ 2) OR strings ("0", "1", "x", "z").
/// - Yosys reserves net id 0/1 for the constants but always emits them as
///   strings in <c>connections</c> / <c>bits</c>, never as integers, so the
///   integer-net path can treat every int as a real net.
/// - The top module is identified by the <c>top</c> attribute (a 32-char
///   binary-encoded "1"). If multiple modules have it set we take the first;
///   if none do we fall back to the only module.
/// - Anonymous nets aren't listed in <c>netnames</c> — that's fine; they
///   still flow through cell connections via their bit ids.
/// </summary>
public static class YosysJsonReader
{
    private static readonly JsonSerializerOptions DefaultJsonOptions = new()
    {
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    public static GateNetlist Read(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        using JsonDocument doc = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip,
        });
        return Read(doc.RootElement);
    }

    public static async Task<GateNetlist> ReadFileAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        await using FileStream stream = File.OpenRead(path);
        using JsonDocument doc = await JsonDocument.ParseAsync(stream, new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip,
        }, cancellationToken);
        return Read(doc.RootElement);
    }

    public static GateNetlist Read(JsonElement root)
    {
        if (!root.TryGetProperty("modules", out JsonElement modulesNode)
            || modulesNode.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("Yosys JSON missing 'modules' object.");
        }

        Dictionary<string, GateModule> modules = new(StringComparer.Ordinal);
        string? top = null;
        foreach (JsonProperty moduleProp in modulesNode.EnumerateObject())
        {
            GateModule module = ReadModule(moduleProp.Name, moduleProp.Value);
            modules[module.Name] = module;
            if (top is null && IsTopAttribute(moduleProp.Value))
            {
                top = module.Name;
            }
        }

        top ??= modules.Keys.FirstOrDefault() ?? "<empty>";
        return new GateNetlist(top, modules);
    }

    private static bool IsTopAttribute(JsonElement moduleNode)
    {
        if (!moduleNode.TryGetProperty("attributes", out JsonElement attrs)
            || attrs.ValueKind != JsonValueKind.Object)
        {
            return false;
        }
        if (!attrs.TryGetProperty("top", out JsonElement topProp))
        {
            return false;
        }
        // Yosys encodes booleans as 32-char binary strings ending in "1" for true.
        return topProp.ValueKind == JsonValueKind.String
            && topProp.GetString() is string s
            && s.Length > 0
            && s[^1] == '1';
    }

    private static GateModule ReadModule(string name, JsonElement moduleNode)
    {
        List<GatePort> ports = [];
        List<GateCell> cells = [];
        List<GateNet> nets = [];

        if (moduleNode.TryGetProperty("ports", out JsonElement portsNode)
            && portsNode.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty portProp in portsNode.EnumerateObject())
            {
                ports.Add(ReadPort(portProp.Name, portProp.Value));
            }
        }

        if (moduleNode.TryGetProperty("cells", out JsonElement cellsNode)
            && cellsNode.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty cellProp in cellsNode.EnumerateObject())
            {
                cells.Add(ReadCell(cellProp.Name, cellProp.Value));
            }
        }

        if (moduleNode.TryGetProperty("netnames", out JsonElement netsNode)
            && netsNode.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty netProp in netsNode.EnumerateObject())
            {
                nets.Add(new GateNet(netProp.Name, ReadBits(netProp.Value)));
            }
        }

        return new GateModule(name, ports, cells, nets);
    }

    private static GatePort ReadPort(string name, JsonElement portNode)
    {
        GatePortDirection direction = GatePortDirection.Input;
        if (portNode.TryGetProperty("direction", out JsonElement dir)
            && dir.ValueKind == JsonValueKind.String)
        {
            direction = ParseDirection(dir.GetString()!);
        }
        return new GatePort(name, direction, ReadBits(portNode));
    }

    private static GateCell ReadCell(string name, JsonElement cellNode)
    {
        string type = cellNode.TryGetProperty("type", out JsonElement t) && t.ValueKind == JsonValueKind.String
            ? t.GetString()!
            : "<unknown>";

        Dictionary<string, GatePortDirection> portDirections = new(StringComparer.Ordinal);
        if (cellNode.TryGetProperty("port_directions", out JsonElement pdir)
            && pdir.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty pd in pdir.EnumerateObject())
            {
                if (pd.Value.ValueKind == JsonValueKind.String)
                {
                    portDirections[pd.Name] = ParseDirection(pd.Value.GetString()!);
                }
            }
        }

        Dictionary<string, GateConnection> connections = new(StringComparer.Ordinal);
        if (cellNode.TryGetProperty("connections", out JsonElement connsNode)
            && connsNode.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty c in connsNode.EnumerateObject())
            {
                IReadOnlyList<GateBit> bits = ReadBitsArray(c.Value);
                connections[c.Name] = new GateConnection(c.Name, bits);
            }
        }

        Dictionary<string, string> parameters = new(StringComparer.Ordinal);
        if (cellNode.TryGetProperty("parameters", out JsonElement paramsNode)
            && paramsNode.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty p in paramsNode.EnumerateObject())
            {
                parameters[p.Name] = p.Value.ValueKind == JsonValueKind.String
                    ? p.Value.GetString() ?? string.Empty
                    : p.Value.ToString();
            }
        }

        return new GateCell(name, type, connections, portDirections, parameters);
    }

    // Reads a "bits" field — accepts both {"bits": [...]} and direct array.
    private static IReadOnlyList<GateBit> ReadBits(JsonElement node)
    {
        if (node.ValueKind == JsonValueKind.Array)
        {
            return ReadBitsArray(node);
        }
        if (node.TryGetProperty("bits", out JsonElement bitsArr)
            && bitsArr.ValueKind == JsonValueKind.Array)
        {
            return ReadBitsArray(bitsArr);
        }
        return Array.Empty<GateBit>();
    }

    private static IReadOnlyList<GateBit> ReadBitsArray(JsonElement arr)
    {
        if (arr.ValueKind != JsonValueKind.Array) return Array.Empty<GateBit>();
        List<GateBit> bits = new(arr.GetArrayLength());
        foreach (JsonElement item in arr.EnumerateArray())
        {
            bits.Add(ParseBit(item));
        }
        return bits;
    }

    private static GateBit ParseBit(JsonElement item) => item.ValueKind switch
    {
        JsonValueKind.Number => GateBit.Net(item.GetInt32()),
        JsonValueKind.String => item.GetString() switch
        {
            "0" => GateBit.ConstantZero,
            "1" => GateBit.ConstantOne,
            "x" or "X" => GateBit.ConstantX,
            "z" or "Z" => GateBit.ConstantZ,
            // Yosys never emits a string-encoded net id, but be defensive.
            string s when int.TryParse(s, out int id) => GateBit.Net(id),
            _ => GateBit.ConstantX,
        },
        _ => GateBit.ConstantX,
    };

    private static GatePortDirection ParseDirection(string raw) => raw switch
    {
        "input" => GatePortDirection.Input,
        "output" => GatePortDirection.Output,
        "inout" => GatePortDirection.InOut,
        _ => GatePortDirection.Input,
    };
}
