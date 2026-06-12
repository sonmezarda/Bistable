using Bistable.App.Services.Routing.Elk;
using Bistable.App.Views;
using Bistable.Core.Projects;

namespace Bistable.Tests.Synthesis;

public sealed class GatePinLabelLayoutTests
{
    [Fact]
    public void Automatic_BelowCompactThreshold_HidesLabels()
    {
        IReadOnlyList<GatePinLabel> labels = GatePinLabelLayout.Resolve(
            Ports("data", 4),
            zoom: 0.54,
            new GatePinLabelDisplayOptions(
                GatePinLabelMode.Automatic,
                GroupBusPinLabels: true,
                CompactZoom: 0.55,
                DetailedZoom: 0.9));

        Assert.Empty(labels);
    }

    [Fact]
    public void Automatic_CompactZoom_GroupsBusRange()
    {
        IReadOnlyList<GatePinLabel> labels = GatePinLabelLayout.Resolve(
            Ports("data", 4),
            zoom: 0.6,
            new GatePinLabelDisplayOptions(
                GatePinLabelMode.Automatic,
                GroupBusPinLabels: false,
                CompactZoom: 0.55,
                DetailedZoom: 0.9));

        GatePinLabel label = Assert.Single(labels);
        Assert.Equal("data[3:0]", label.Text);
    }

    [Fact]
    public void Automatic_DetailedZoom_Ungrouped_ShowsEveryBit()
    {
        IReadOnlyList<GatePinLabel> labels = GatePinLabelLayout.Resolve(
            Ports("data", 4),
            zoom: 1.0,
            new GatePinLabelDisplayOptions(
                GatePinLabelMode.Automatic,
                GroupBusPinLabels: false,
                CompactZoom: 0.55,
                DetailedZoom: 0.9));

        Assert.Equal(
            ["data[0]", "data[1]", "data[2]", "data[3]"],
            labels.Select(static label => label.Text));
    }

    [Fact]
    public void Always_Grouped_ShowsBusBelowAutomaticThreshold()
    {
        IReadOnlyList<GatePinLabel> labels = GatePinLabelLayout.Resolve(
            Ports("operand", 8),
            zoom: 0.1,
            new GatePinLabelDisplayOptions(
                GatePinLabelMode.Always,
                GroupBusPinLabels: true,
                CompactZoom: 0.55,
                DetailedZoom: 0.9));

        Assert.Equal("operand[7:0]", Assert.Single(labels).Text);
    }

    [Fact]
    public void Hidden_SuppressesLabelsAtAnyZoom()
    {
        IReadOnlyList<GatePinLabel> labels = GatePinLabelLayout.Resolve(
            Ports("data", 2),
            zoom: 8.0,
            new GatePinLabelDisplayOptions(
                GatePinLabelMode.Hidden,
                GroupBusPinLabels: false,
                CompactZoom: 0.05,
                DetailedZoom: 0.05));

        Assert.Empty(labels);
    }

    [Fact]
    public void Normalize_ClampsThresholdsAndPreservesOrdering()
    {
        GatePinLabelDisplayOptions normalized = new GatePinLabelDisplayOptions(
            GatePinLabelMode.Automatic,
            GroupBusPinLabels: true,
            CompactZoom: 2.0,
            DetailedZoom: 0.4).Normalize();

        Assert.Equal(2.0, normalized.CompactZoom);
        Assert.Equal(2.0, normalized.DetailedZoom);
        Assert.Equal(GatePinVisibilityMode.All, normalized.VisibilityMode);
    }

    private static IReadOnlyList<ElkPort> Ports(string baseName, int width) =>
        Enumerable.Range(0, width)
            .Select(index => new ElkPort
            {
                Id = $"node.{baseName}[{index}]",
                Y = index * 18,
                LayoutOptions = new Dictionary<string, string>
                {
                    ["elk.port.side"] = "WEST",
                },
                Labels = [new ElkLabel { Text = $"{baseName}[{index}]" }],
            })
            .ToArray();
}
