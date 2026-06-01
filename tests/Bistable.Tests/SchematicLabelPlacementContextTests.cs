using Avalonia;
using Bistable.App.Services;

namespace Bistable.Tests;

public sealed class SchematicLabelPlacementContextTests
{
    [Fact]
    public void PlaceLabel_WhenPreferredOverlapsWire_UsesClearAlternative()
    {
        SchematicLabelPlacementContext placement = new();
        placement.AddWirePolyline([new Point(0, 10), new Point(100, 10)], padding: 3);

        Rect placed = placement.PlaceLabel(
            new Size(30, 10),
            [
                new Point(20, 6),  // overlaps the horizontal wire obstacle
                new Point(20, -12)  // clear, Vivado-style just above the wire
            ]);

        Assert.Equal(new Point(20, -12), new Point(placed.X, placed.Y));
    }

    [Fact]
    public void PlaceLabel_AvoidsAlreadyPlacedLabels()
    {
        SchematicLabelPlacementContext placement = new();
        Rect first = placement.PlaceLabel(
            new Size(30, 10),
            [new Point(20, 6)]);

        Rect second = placement.PlaceLabel(
            new Size(30, 10),
            [
                new Point(20, 6),
                new Point(20, 22)
            ]);

        Assert.False(first.Inflate(2).Intersects(second));
        Assert.Equal(new Point(20, 22), new Point(second.X, second.Y));
    }
}
