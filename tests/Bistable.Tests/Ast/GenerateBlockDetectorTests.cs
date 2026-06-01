using Bistable.Core.Design.Ast;

namespace Bistable.Tests.Ast;

/// <summary>
/// P2.6-2 minimal: pattern-detect Verilator-unrolled generate cells. The
/// detector is layout-agnostic; it just groups instances whose names match
/// <c>label[N]</c> or <c>label[N].rest</c>.
/// </summary>
public sealed class GenerateBlockDetectorTests
{
    [Fact]
    public void Detect_NoGenerateBlocks_ReturnsEmpty()
    {
        InstanceDecl[] instances =
        [
            new("u_alu", "alu", []),
            new("u_decoder", "decoder", []),
        ];

        IReadOnlyList<GenerateGroup> groups = GenerateBlockDetector.DetectGroups(instances);

        Assert.Empty(groups);
    }

    [Fact]
    public void Detect_FourMembers_ProducesOneGroupWithSortedMembers()
    {
        InstanceDecl[] instances =
        [
            new("g[2].inst", "leaf", []),
            new("g[0].inst", "leaf", []),
            new("g[3].inst", "leaf", []),
            new("g[1].inst", "leaf", []),
        ];

        GenerateGroup group = Assert.Single(GenerateBlockDetector.DetectGroups(instances));

        Assert.Equal("g", group.Label);
        Assert.Equal(0, group.LowIndex);
        Assert.Equal(3, group.HighIndex);
        Assert.Equal(4, group.Members.Count);
        Assert.Equal([0, 1, 2, 3], group.Members.Select(m => m.Index).ToArray());
    }

    [Fact]
    public void Detect_SingleMember_IgnoredAsUninteresting()
    {
        InstanceDecl[] instances = [new("g[0].inst", "leaf", [])];
        Assert.Empty(GenerateBlockDetector.DetectGroups(instances));
    }

    [Fact]
    public void Detect_TwoDifferentLabels_ProducesTwoGroups()
    {
        InstanceDecl[] instances =
        [
            new("g[0].alu", "alu", []),
            new("g[1].alu", "alu", []),
            new("h[0].dec", "dec", []),
            new("h[1].dec", "dec", []),
        ];

        IReadOnlyList<GenerateGroup> groups = GenerateBlockDetector.DetectGroups(instances);

        Assert.Equal(2, groups.Count);
        Assert.Contains(groups, g => g.Label == "g");
        Assert.Contains(groups, g => g.Label == "h");
    }

    [Fact]
    public void Detect_GenerateMixedWithStandalone_SeparatesCorrectly()
    {
        InstanceDecl[] instances =
        [
            new("g[0].rf", "rf", []),
            new("g[1].rf", "rf", []),
            new("u_top", "top", []),                 // not generate
        ];

        GenerateGroup group = Assert.Single(GenerateBlockDetector.DetectGroups(instances));
        Assert.DoesNotContain(group.Members, m => m.Instance.InstanceName == "u_top");
    }

    [Fact]
    public void Detect_LabelWithoutDotSuffix_StillRecognised()
    {
        // Some generates have no inner instance — the cell is the iteration.
        InstanceDecl[] instances =
        [
            new("g[0]", "leaf", []),
            new("g[1]", "leaf", []),
        ];

        GenerateGroup group = Assert.Single(GenerateBlockDetector.DetectGroups(instances));
        Assert.Equal("g", group.Label);
    }
}
