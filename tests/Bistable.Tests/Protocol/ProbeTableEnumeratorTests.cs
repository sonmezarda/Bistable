using Bistable.Core.Design;
using Bistable.Core.Design.Ast;
using Bistable.Verilator;

namespace Bistable.Tests.Protocol;

/// <summary>
/// Phase 3 P3-3: <see cref="ProbeTableEnumerator"/> walks a <see cref="DesignAst"/>
/// and produces the entries the C++ probe table is built from. These tests
/// pin the enumeration semantics (path format, filters, hierarchy traversal)
/// without needing Verilator.
/// </summary>
public sealed class ProbeTableEnumeratorTests
{
    private static ModuleAst MakeModule(
        string name,
        bool isTop = false,
        IReadOnlyList<PortDecl>? ports = null,
        IReadOnlyList<SignalDecl>? locals = null,
        IReadOnlyList<InstanceDecl>? instances = null) =>
        new(
            Name: name, IsTop: isTop,
            Ports: ports ?? [],
            Parameters: [],
            LocalSignals: locals ?? [],
            Instances: instances ?? [],
            ContAssigns: [], SequentialBlocks: [], CombinationalBlocks: []);

    // ── Path mangling ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("top",                   "top")]
    [InlineData("top.q",                 "top__DOT__q")]
    [InlineData("top.acc.reg_q",         "top__DOT__acc__DOT__reg_q")]
    [InlineData("top.cpu.alu.add.sum",   "top__DOT__cpu__DOT__alu__DOT__add__DOT__sum")]
    public void MangleFieldName_DotSeparatorBecomesDoubleUnderscoreDot(string path, string expectedField)
    {
        Assert.Equal(expectedField, ProbeTableEnumerator.MangleFieldName(path));
    }

    // ── Top-level enumeration ────────────────────────────────────────────

    [Fact]
    public void Enumerate_TopOnly_EmitsPortsAndLocals_WithTopPrefixedPaths()
    {
        DesignAst ast = new(Modules: [
            MakeModule("top", isTop: true,
                ports:  [new PortDecl("clk", SignalDirection.Input, 1, false, 1),
                         new PortDecl("q",   SignalDirection.Output, 8, false, 2)],
                locals: [new SignalDecl("internal_reg", 8, false, []),
                         new SignalDecl("internal_wire", 1, false, [])])
        ]);

        List<ProbeEntry> entries = ProbeTableEnumerator.Enumerate(ast, "top").ToList();

        Assert.Equal(4, entries.Count);
        Assert.Contains(entries, e => e.Path == "top.clk" && e.Width == 1);
        Assert.Contains(entries, e => e.Path == "top.q" && e.Width == 8);
        Assert.Contains(entries, e => e.Path == "top.internal_reg");
        Assert.Contains(entries, e => e.Path == "top.internal_wire");
    }

    // ── Hierarchy traversal ──────────────────────────────────────────────

    [Fact]
    public void Enumerate_NestedInstances_PathsTraverseHierarchy()
    {
        ModuleAst inner = MakeModule("counter",
            ports: [new PortDecl("clk", SignalDirection.Input, 1, false, 1)],
            locals: [new SignalDecl("count", 8, false, [], IsRegistered: true)]);
        ModuleAst top = MakeModule("top", isTop: true,
            ports: [new PortDecl("clk", SignalDirection.Input, 1, false, 1)],
            instances: [new InstanceDecl("counter_i", "counter", [])]);

        DesignAst ast = new([top, inner]);

        List<ProbeEntry> entries = ProbeTableEnumerator.Enumerate(ast, "top").ToList();

        Assert.Contains(entries, e => e.Path == "top.clk");
        Assert.Contains(entries, e => e.Path == "top.counter_i.clk");
        Assert.Contains(entries, e => e.Path == "top.counter_i.count" && e.IsRegistered);
    }

    [Fact]
    public void Enumerate_DeepHierarchy_AllLevelsTraversed()
    {
        ModuleAst leaf = MakeModule("leaf",
            locals: [new SignalDecl("q", 8, false, [])]);
        ModuleAst mid  = MakeModule("mid",
            instances: [new InstanceDecl("leaf_a", "leaf", []),
                        new InstanceDecl("leaf_b", "leaf", [])]);
        ModuleAst top  = MakeModule("top", isTop: true,
            instances: [new InstanceDecl("mid_i", "mid", [])]);

        DesignAst ast = new([top, mid, leaf]);
        List<ProbeEntry> entries = ProbeTableEnumerator.Enumerate(ast, "top").ToList();

        Assert.Contains(entries, e => e.Path == "top.mid_i.leaf_a.q");
        Assert.Contains(entries, e => e.Path == "top.mid_i.leaf_b.q");
    }

    [Fact]
    public void Enumerate_FieldNamesMatchVerilatorMangling()
    {
        ModuleAst sub = MakeModule("sub",
            locals: [new SignalDecl("reg_q", 8, false, [], IsRegistered: true)]);
        ModuleAst top = MakeModule("top", isTop: true,
            instances: [new InstanceDecl("acc", "sub", [])]);

        DesignAst ast = new([top, sub]);
        ProbeEntry entry = Assert.Single(
            ProbeTableEnumerator.Enumerate(ast, "top"),
            e => e.Path == "top.acc.reg_q");

        Assert.Equal("top__DOT__acc__DOT__reg_q", entry.FieldName);
    }

    // ── Filters ──────────────────────────────────────────────────────────

    [Fact]
    public void Enumerate_VerilatorInternalSignals_AreFiltered()
    {
        ModuleAst top = MakeModule("top", isTop: true,
            locals: [
                new SignalDecl("user_signal",         8, false, []),
                new SignalDecl("__VdfgTmp_h1234__0",  8, false, []),
                new SignalDecl("__Vlvbound_h5__1",    8, false, []),
            ]);
        DesignAst ast = new([top]);

        List<string> paths = ProbeTableEnumerator.Enumerate(ast, "top").Select(e => e.Path).ToList();

        Assert.Contains("top.user_signal", paths);
        Assert.DoesNotContain(paths, p => p.Contains("__V"));
    }

    [Fact]
    public void Enumerate_WideSignals_AreFiltered_PendingVlWideSupport()
    {
        // Width > 64 = VlWide<N> on the C++ side, needs hex-string protocol.
        // Until that path lands the enumerator must NOT emit a probe whose
        // C++ cast to uint64_t would silently truncate the value.
        ModuleAst top = MakeModule("top", isTop: true,
            locals: [
                new SignalDecl("scalar",  32,  false, []),
                new SignalDecl("wide",    128, false, []),
                new SignalDecl("borderline", 64, false, []),
            ]);
        DesignAst ast = new([top]);

        List<string> paths = ProbeTableEnumerator.Enumerate(ast, "top").Select(e => e.Path).ToList();
        Assert.Contains("top.scalar", paths);
        Assert.Contains("top.borderline", paths);   // exactly 64 OK
        Assert.DoesNotContain("top.wide", paths);
    }

    [Fact]
    public void Enumerate_SingleDimensionMemory_EmitsMemoryEntry_WithCorrectDepth()
    {
        ModuleAst top = MakeModule("top", isTop: true,
            locals: [
                new SignalDecl("scalar", 8, false, []),
                new SignalDecl("mem",    8, false, [new BitRange(15, 0)]),   // 16-cell memory
            ]);
        DesignAst ast = new([top]);

        List<ProbeEntry> entries = ProbeTableEnumerator.Enumerate(ast, "top").ToList();
        Assert.Contains(entries, e => e.Path == "top.scalar" && !e.IsMemory);
        ProbeEntry mem = Assert.Single(entries, e => e.Path == "top.mem");
        Assert.True(mem.IsMemory);
        Assert.Equal(16, mem.MemoryDepth);
        Assert.Equal(8, mem.Width);
    }

    [Fact]
    public void Enumerate_MultiDimensionMemory_IsStillFiltered_BeyondP36Scope()
    {
        ModuleAst top = MakeModule("top", isTop: true,
            locals: [
                new SignalDecl("scalar", 8, false, []),
                new SignalDecl("mem2d",  8, false, [new BitRange(3, 0), new BitRange(15, 0)]),
            ]);
        DesignAst ast = new([top]);

        List<string> paths = ProbeTableEnumerator.Enumerate(ast, "top").Select(e => e.Path).ToList();
        Assert.Contains("top.scalar", paths);
        Assert.DoesNotContain("top.mem2d", paths);
    }

    // ── Edge cases ───────────────────────────────────────────────────────

    [Fact]
    public void Enumerate_UnknownTopModule_YieldsNothing_NoExceptions()
    {
        DesignAst ast = new([MakeModule("foo")]);
        List<ProbeEntry> entries = ProbeTableEnumerator.Enumerate(ast, "nonexistent").ToList();
        Assert.Empty(entries);
    }

    [Fact]
    public void Enumerate_InstanceReferencingUnknownModule_SkipsSilently()
    {
        // Verilator XML quirks can produce dangling instance refs. We tolerate
        // them (skip with no warning) rather than throw — the user's design
        // is still rendered as best as possible.
        ModuleAst top = MakeModule("top", isTop: true,
            instances: [new InstanceDecl("ghost", "nonexistent_module", [])]);
        DesignAst ast = new([top]);

        List<ProbeEntry> entries = ProbeTableEnumerator.Enumerate(ast, "top").ToList();
        // No exception, no entries for the ghost subtree
        Assert.DoesNotContain(entries, e => e.Path.Contains("ghost"));
    }
}
