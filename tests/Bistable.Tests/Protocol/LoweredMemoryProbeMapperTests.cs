using Bistable.Core.Design.Ast;
using Bistable.Verilator;

namespace Bistable.Tests.Protocol;

public sealed class LoweredMemoryProbeMapperTests
{
    [Fact]
    public void Build_UsesSourceArraySemantics_ForArbitrarilyNamedMemory()
    {
        DesignAst source = SourceDesign("storage_bank", depth: 4);
        DesignAst synthesized = SynthesizedDesign(
            "storage_bank",
            Enumerable.Range(0, 4));

        LoweredMemoryProbeMap result = LoweredMemoryProbeMapper.Build(
            source,
            synthesized,
            "top");

        ProbeEntry probe = Assert.Single(result.SupplementalProbes);
        Assert.Equal("top.u_storage.storage_bank", probe.Path);
        Assert.True(probe.IsMemory);
        Assert.Equal(4, probe.MemoryDepth);
        Assert.Equal(
            [
                "top__DOT__u_storage__02estorage_bank__05b0__05d",
                "top__DOT__u_storage__02estorage_bank__05b1__05d",
                "top__DOT__u_storage__02estorage_bank__05b2__05d",
                "top__DOT__u_storage__02estorage_bank__05b3__05d",
            ],
            probe.MemoryElementFieldNames);

        GateMemoryProbeMapping mapping = Assert.Single(result.Manifest.Memories);
        Assert.Equal(GateMemoryMappingKind.LoweredElements, mapping.Kind);
        Assert.Empty(result.Manifest.UnresolvedMemories);
    }

    [Fact]
    public void Build_DoesNotInferUnrelatedIndexedScalarsAsMemory()
    {
        DesignAst source = SourceDesign("declared_array", depth: 2);
        DesignAst synthesized = SynthesizedDesign(
            "unrelated_signal",
            Enumerable.Range(0, 2));

        LoweredMemoryProbeMap result = LoweredMemoryProbeMapper.Build(
            source,
            synthesized,
            "top");

        Assert.Empty(result.SupplementalProbes);
        GateMemoryProbeMapping unresolved = Assert.Single(result.Manifest.UnresolvedMemories);
        Assert.Equal("top.u_storage.declared_array", unresolved.LogicalPath);
    }

    [Fact]
    public void Build_ReportsPartialMapping_InsteadOfPublishingCorruptMemory()
    {
        DesignAst source = SourceDesign("storage_bank", depth: 4);
        DesignAst synthesized = SynthesizedDesign("storage_bank", [0, 1, 3]);

        LoweredMemoryProbeMap result = LoweredMemoryProbeMapper.Build(
            source,
            synthesized,
            "top");

        Assert.Empty(result.SupplementalProbes);
        GateMemoryProbeMapping unresolved = Assert.Single(result.Manifest.UnresolvedMemories);
        Assert.Contains("Resolved 3 of 4", unresolved.Diagnostic, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_ReportsUnsupportedWideMemory_Explicitly()
    {
        ModuleAst top = Module(
            "top",
            isTop: true,
            locals: [new SignalDecl("wide_storage", 128, false, [new BitRange(3, 0)])]);

        LoweredMemoryProbeMap result = LoweredMemoryProbeMapper.Build(
            new DesignAst([top]),
            new DesignAst([Module("top", isTop: true)]),
            "top");

        GateMemoryProbeMapping unresolved = Assert.Single(result.Manifest.UnresolvedMemories);
        Assert.Contains("128", unresolved.Diagnostic, StringComparison.Ordinal);
        Assert.Contains("64-bit", unresolved.Diagnostic, StringComparison.Ordinal);
    }

    private static DesignAst SourceDesign(string memoryName, int depth)
    {
        ModuleAst storage = Module(
            "storage",
            isTop: false,
            locals: [new SignalDecl(memoryName, 8, false, [new BitRange(depth - 1, 0)])]);
        ModuleAst top = Module(
            "top",
            isTop: true,
            instances: [new InstanceDecl("u_storage", "storage", [])]);
        return new DesignAst([top, storage]);
    }

    private static DesignAst SynthesizedDesign(
        string memoryName,
        IEnumerable<int> addresses)
    {
        SignalDecl[] locals = addresses
            .Select(address => new SignalDecl(
                $"u_storage.{memoryName}[{address}]",
                8,
                false,
                [],
                OrigName: $"u_storage__02e{memoryName}__05b{address}__05d"))
            .ToArray();
        return new DesignAst([Module("top", isTop: true, locals: locals)]);
    }

    private static ModuleAst Module(
        string name,
        bool isTop,
        IReadOnlyList<SignalDecl>? locals = null,
        IReadOnlyList<InstanceDecl>? instances = null) =>
        new(
            name,
            isTop,
            Ports: [],
            Parameters: [],
            LocalSignals: locals ?? [],
            Instances: instances ?? [],
            ContAssigns: [],
            SequentialBlocks: [],
            CombinationalBlocks: []);
}
