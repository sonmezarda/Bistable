using System.Text.Json.Serialization;

namespace Bistable.App.Services.Routing.Elk;

// Strongly-typed contract that mirrors the elkjs JSON graph format.
// Bistable builds an ElkGraph in-process, serialises it to JSON, hands it to the
// elk-router Node script, and reads back ElkGraph with x/y/sections populated.

public sealed class ElkGraph
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "root";

    [JsonPropertyName("layoutOptions")]
    public Dictionary<string, string>? LayoutOptions { get; set; }

    [JsonPropertyName("children")]
    public List<ElkNode> Children { get; set; } = [];

    [JsonPropertyName("edges")]
    public List<ElkEdge> Edges { get; set; } = [];

    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("y")]
    public double Y { get; set; }

    [JsonPropertyName("width")]
    public double Width { get; set; }

    [JsonPropertyName("height")]
    public double Height { get; set; }
}

public sealed class ElkNode
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("width")]
    public double Width { get; set; }

    [JsonPropertyName("height")]
    public double Height { get; set; }

    [JsonPropertyName("layoutOptions")]
    public Dictionary<string, string>? LayoutOptions { get; set; }

    [JsonPropertyName("labels")]
    public List<ElkLabel>? Labels { get; set; }

    [JsonPropertyName("ports")]
    public List<ElkPort>? Ports { get; set; }

    [JsonPropertyName("children")]
    public List<ElkNode>? Children { get; set; }

    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("y")]
    public double Y { get; set; }
}

public sealed class ElkPort
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("width")]
    public double Width { get; set; }

    [JsonPropertyName("height")]
    public double Height { get; set; }

    [JsonPropertyName("layoutOptions")]
    public Dictionary<string, string>? LayoutOptions { get; set; }

    [JsonPropertyName("labels")]
    public List<ElkLabel>? Labels { get; set; }

    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("y")]
    public double Y { get; set; }
}

public sealed class ElkLabel
{
    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    [JsonPropertyName("width")]
    public double Width { get; set; }

    [JsonPropertyName("height")]
    public double Height { get; set; }

    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("y")]
    public double Y { get; set; }
}

public sealed class ElkEdge
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("sources")]
    public List<string> Sources { get; set; } = [];

    [JsonPropertyName("targets")]
    public List<string> Targets { get; set; } = [];

    [JsonPropertyName("labels")]
    public List<ElkLabel>? Labels { get; set; }

    [JsonPropertyName("layoutOptions")]
    public Dictionary<string, string>? LayoutOptions { get; set; }

    [JsonPropertyName("sections")]
    public List<ElkEdgeSection>? Sections { get; set; }

    [JsonPropertyName("junctionPoints")]
    public List<ElkPoint>? JunctionPoints { get; set; }
}

public sealed class ElkEdgeSection
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("startPoint")]
    public ElkPoint StartPoint { get; set; } = new();

    [JsonPropertyName("endPoint")]
    public ElkPoint EndPoint { get; set; } = new();

    [JsonPropertyName("bendPoints")]
    public List<ElkPoint>? BendPoints { get; set; }

    [JsonPropertyName("incomingShape")]
    public string? IncomingShape { get; set; }

    [JsonPropertyName("outgoingShape")]
    public string? OutgoingShape { get; set; }
}

public sealed class ElkPoint
{
    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("y")]
    public double Y { get; set; }
}
