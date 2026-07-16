using System.Text.Json.Serialization;

namespace Bistable.Protocol;

/// <summary>Per-path outcome returned by a batch signal read.</summary>
public sealed record SignalReadOutcome(
    string Path,
    string? Value,
    int Width,
    bool IsSigned,
    string? Error)
{
    [JsonIgnore]
    public bool IsSuccess => Error is null;
}

/// <summary>
/// Result of one or more worker-side batch reads. A missing path produces a
/// failed outcome without failing successful paths in the same request.
/// </summary>
public sealed record SignalsReadResult(IReadOnlyList<SignalReadOutcome> Results);
