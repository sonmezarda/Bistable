using Avalonia;
using Bistable.App.Views;

namespace Bistable.Tests;

/// <summary>
/// Phase 2.5 P2.5-1: top-level module symbol boundary pin layout.
/// Pre-fix bug: output pins rendered as [module]→wire→[label]………[badge] with
/// the badge floating up to 64px past the wire end — wire looked disconnected
/// from the value. Fix: mirror the input layout exactly so badge sits 6px past
/// the wire end and label sits 10px past the badge.
/// These tests are pure-geometry against the extracted helper — they don't
/// touch Avalonia rendering and run in any environment.
/// </summary>
public sealed class BoundaryPinLayoutTests
{
    // Standard fixture: 1000×400 viewport with a 300×200 module centered.
    private static readonly Rect Bounds = new(0, 0, 1000, 400);
    private static readonly Rect ModuleRect = new(350, 100, 300, 200);
    private const double Y = 200;
    private const double BadgeWidth = 80;
    private const string Label = "pc_out";

    private static double FakeMeasure(string lbl, double max) => Math.Min(lbl.Length * 7.0, max);

    private static SchematicPreviewControl.BoundaryPinLayout Input() =>
        SchematicPreviewControl.ComputeBoundaryPinLayout(
            Bounds, ModuleRect, Y, BadgeWidth, leftSide: true, Label, FakeMeasure);

    private static SchematicPreviewControl.BoundaryPinLayout Output() =>
        SchematicPreviewControl.ComputeBoundaryPinLayout(
            Bounds, ModuleRect, Y, BadgeWidth, leftSide: false, Label, FakeMeasure);

    // ── Input side: badge adjacent to wire start (existing behaviour) ─────

    [Fact]
    public void Input_Badge_SitsImmediatelyLeftOfWireStart()
    {
        var layout = Input();
        double pinStartX = layout.WireStart.X;
        // Badge right edge should be 6px to the left of the wire start
        double gap = pinStartX - layout.Badge.Right;
        Assert.InRange(gap, 5.9, 6.1);
    }

    [Fact]
    public void Input_Label_SitsLeftOfBadge()
    {
        var layout = Input();
        Assert.True(layout.LabelX < layout.Badge.X,
            $"Input label (x={layout.LabelX}) should be left of badge (x={layout.Badge.X})");
    }

    // ── Output side: badge adjacent to wire end (P2.5-1 fix) ──────────────

    [Fact]
    public void Output_Badge_SitsImmediatelyRightOfWireEnd()
    {
        var layout = Output();
        double pinEndX = layout.WireEnd.X;
        double gap = layout.Badge.X - pinEndX;
        Assert.InRange(gap, 5.9, 6.1);
    }

    [Fact]
    public void Output_Label_SitsRightOfBadge()
    {
        var layout = Output();
        Assert.True(layout.LabelX > layout.Badge.Right,
            $"Output label (x={layout.LabelX}) should be right of badge (right={layout.Badge.Right})");
    }

    // ── Symmetry: identical gaps on both sides (the core P2.5-1 contract) ──

    [Fact]
    public void InputAndOutput_BadgeToWireGap_IsSymmetric()
    {
        var input = Input();
        var output = Output();

        double inputGap  = input.WireStart.X - input.Badge.Right;   // wire start - badge right
        double outputGap = output.Badge.X - output.WireEnd.X;       // badge left - wire end

        Assert.Equal(inputGap, outputGap, precision: 1);
    }

    [Fact]
    public void InputAndOutput_BadgeWidth_IsIdentical()
    {
        var input = Input();
        var output = Output();
        Assert.Equal(input.Badge.Width, output.Badge.Width);
    }

    [Fact]
    public void InputAndOutput_WireLength_IsIdentical()
    {
        var input = Input();
        var output = Output();
        double inputWire = input.WireEnd.X - input.WireStart.X;
        double outputWire = output.WireEnd.X - output.WireStart.X;
        Assert.Equal(inputWire, outputWire);
    }

    // ── Wire endpoints: anchored to module edges ──────────────────────────

    [Fact]
    public void Input_WireEnd_AnchoredAtModuleLeftEdge()
    {
        var layout = Input();
        Assert.Equal(ModuleRect.X, layout.WireEnd.X);
    }

    [Fact]
    public void Output_WireStart_AnchoredAtModuleRightEdge()
    {
        var layout = Output();
        Assert.Equal(ModuleRect.Right, layout.WireStart.X);
    }

    // ── Hit test rect covers badge + label ────────────────────────────────

    [Fact]
    public void Output_HitRect_CoversBadgeAndLabel()
    {
        var layout = Output();
        // Hit rect should span from module edge to past the label
        Assert.True(layout.Hit.Right >= layout.Badge.Right,
            "Hit rect must extend at least to badge right edge");
    }

    // ── Long labels: ellipsized inside bounds ─────────────────────────────

    [Fact]
    public void LongLabel_EllipsizedToFitWithinBounds()
    {
        // Use a measure func that records what label was passed
        string passedLabel = "";
        double passedMax = 0;
        SchematicPreviewControl.ComputeBoundaryPinLayout(
            Bounds, ModuleRect, Y, BadgeWidth, leftSide: false,
            rawLabel: "very_long_signal_name_that_should_be_truncated",
            measureLabel: (lbl, max) => { passedLabel = lbl; passedMax = max; return Math.Min(lbl.Length * 7.0, max); });

        Assert.Equal("very_long_signal_name_that_should_be_truncated", passedLabel);
        Assert.True(passedMax > 0, "Label max width must be positive");
        Assert.True(passedMax <= Bounds.Width, "Label max width must fit within bounds");
    }
}
