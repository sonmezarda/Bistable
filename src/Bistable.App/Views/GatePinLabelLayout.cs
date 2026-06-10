using Bistable.App.Services.Routing.Elk;
using Bistable.Core.Projects;

namespace Bistable.App.Views;

internal static class GatePinLabelLayout
{
    public static IReadOnlyList<GatePinLabel> Resolve(
        IReadOnlyList<ElkPort>? ports,
        double zoom,
        GatePinLabelDisplayOptions options)
    {
        if (ports is null || ports.Count == 0 || options.Mode == GatePinLabelMode.Hidden)
        {
            return [];
        }

        bool detailed = options.Mode == GatePinLabelMode.Always
            || zoom >= options.DetailedZoom;
        bool compact = options.Mode == GatePinLabelMode.Always
            || zoom >= options.CompactZoom;
        if (!compact)
        {
            return [];
        }

        IReadOnlyList<GatePinLabelCandidate> candidates = ports
            .Select(CreateCandidate)
            .Where(static candidate => candidate is not null)
            .Cast<GatePinLabelCandidate>()
            .ToArray();

        if (detailed && !options.GroupBusPinLabels)
        {
            return candidates
                .Select(static candidate => new GatePinLabel(
                    candidate.Port,
                    candidate.DisplayName,
                    candidate.IsWestSide))
                .ToArray();
        }

        return candidates
            .GroupBy(
                static candidate => (candidate.BaseName, candidate.IsWestSide),
                static candidate => candidate)
            .Select(BuildGroupedLabel)
            .ToArray();
    }

    private static GatePinLabel BuildGroupedLabel(
        IGrouping<(string BaseName, bool IsWestSide), GatePinLabelCandidate> group)
    {
        GatePinLabelCandidate[] candidates = group
            .OrderBy(static candidate => candidate.Port.Y)
            .ToArray();
        GatePinLabelCandidate representative = candidates[candidates.Length / 2];
        int[] indices = candidates
            .Where(static candidate => candidate.BitIndex.HasValue)
            .Select(static candidate => candidate.BitIndex!.Value)
            .ToArray();
        string text = indices.Length > 1
            ? $"{group.Key.BaseName}[{indices.Max()}:{indices.Min()}]"
            : candidates[0].DisplayName;
        return new GatePinLabel(representative.Port, text, group.Key.IsWestSide);
    }

    private static GatePinLabelCandidate? CreateCandidate(ElkPort port)
    {
        string? displayName = port.Labels?.FirstOrDefault()?.Text;
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return null;
        }

        bool isWestSide = string.Equals(
            port.LayoutOptions?.GetValueOrDefault("elk.port.side"),
            "WEST",
            StringComparison.OrdinalIgnoreCase);
        (string baseName, int? bitIndex) = ParseBitName(displayName);
        return new GatePinLabelCandidate(port, displayName, baseName, bitIndex, isWestSide);
    }

    private static (string BaseName, int? BitIndex) ParseBitName(string displayName)
    {
        int open = displayName.LastIndexOf('[');
        if (open <= 0 || !displayName.EndsWith(']'))
        {
            return (displayName, null);
        }

        string indexText = displayName[(open + 1)..^1];
        return int.TryParse(indexText, out int index)
            ? (displayName[..open], index)
            : (displayName, null);
    }

    private sealed record GatePinLabelCandidate(
        ElkPort Port,
        string DisplayName,
        string BaseName,
        int? BitIndex,
        bool IsWestSide);
}

internal sealed record GatePinLabel(
    ElkPort Port,
    string Text,
    bool IsWestSide);

public sealed record GatePinLabelDisplayOptions(
    GatePinLabelMode Mode,
    bool GroupBusPinLabels,
    double CompactZoom,
    double DetailedZoom)
{
    public static GatePinLabelDisplayOptions Default { get; } =
        new(GatePinLabelMode.Automatic, true, 0.55, 0.9);

    public GatePinLabelDisplayOptions Normalize()
    {
        double compact = Math.Clamp(CompactZoom, 0.05, 8.0);
        double detailed = Math.Clamp(DetailedZoom, compact, 8.0);
        return this with
        {
            CompactZoom = compact,
            DetailedZoom = detailed,
        };
    }
}
