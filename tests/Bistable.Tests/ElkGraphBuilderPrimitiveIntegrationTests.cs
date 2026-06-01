using System.IO;
using Bistable.App.Services.Routing.Elk;
using Bistable.App.ViewModels;
using Bistable.Core.Design;
using Bistable.Core.Design.Ast;
using Bistable.Core.Design.Schematic;
using Bistable.Verilator;

namespace Bistable.Tests;

/// <summary>
/// Phase 2 P2-4c integration: feeds a Verilator-style XML through the full pipeline
/// (XML → AST → Decode → ElkGraphBuilder.Build with Primitives) and asserts the rendered
/// graph contains the expected primitive nodes and edges.
///
/// These tests guard against regressions across the whole Phase 1 + 2 stack at once.
/// </summary>
public sealed class ElkGraphBuilderPrimitiveIntegrationTests
{
    // ── End-to-end: arnicomp-style register ───────────────────────────────

    [Fact]
    public void EndToEnd_RegisterCellWithAsyncReset_RendersFFNodeAndWires()
    {
        ModuleAst topModule = ParseTop("""
            <var name="clk"    dtype_id="1" dir="input"  pinIndex="1" vartype="logic"/>
            <var name="rst_n"  dtype_id="1" dir="input"  pinIndex="2" vartype="logic"/>
            <var name="d_in"   dtype_id="8" dir="input"  pinIndex="3" vartype="logic"/>
            <var name="d_out"  dtype_id="8" dir="output" pinIndex="4" vartype="logic"/>
            <var name="q"      dtype_id="8" vartype="logic"/>
            <always>
              <sentree>
                <senitem edgeType="POS"><varref name="clk"/></senitem>
                <senitem edgeType="NEG"><varref name="rst_n"/></senitem>
              </sentree>
              <assigndly dtype_id="8">
                <cond>
                  <varref name="rst_n"/>
                  <varref name="d_in"/>
                  <const name="8'h0"/>
                </cond>
                <varref name="q"/>
              </assigndly>
            </always>
            <contassign dtype_id="8">
              <varref name="q"/>
              <varref name="d_out"/>
            </contassign>
            """);

        ElkBuildResult result = BuildFromModule(topModule);

        // FF node present
        ElkNode ffNode = Assert.Single(result.Graph.Children, n => ElkNodeIds.IsFlipFlop(n.Id));
        Assert.NotNull(ffNode.Ports);
        Assert.Equal(4, ffNode.Ports!.Count);   // D, Clk, Rst, Q

        // Wires: d_in → FF.D, clk → FF.Clk, rst_n → FF.Rst, FF.Q → d_out (via the q wire alias)
        Assert.Contains(result.Graph.Edges, e =>
            e.Sources.Contains("boundary_in.d_in") &&
            e.Targets.Any(t => t.EndsWith(".d")));
        Assert.Contains(result.Graph.Edges, e =>
            e.Sources.Contains("boundary_in.clk") &&
            e.Targets.Any(t => t.EndsWith(".clk")));
        Assert.Contains(result.Graph.Edges, e =>
            e.Sources.Contains("boundary_in.rst_n") &&
            e.Targets.Any(t => t.EndsWith(".rst")));
        // After P2-4d, the `assign d_out = q;` wire alias becomes a BufferPrimitive.
        // FF.Q drives the buffer's input, and the buffer's output drives boundary_out.
        Assert.Contains(result.Graph.Edges, e =>
            e.Sources.Any(s => s.EndsWith(".q")) &&
            e.Targets.Any(t => t.StartsWith("buf_d_out") && t.EndsWith(".in")));
        Assert.Contains(result.Graph.Edges, e =>
            e.Sources.Any(s => s.StartsWith("buf_d_out") && s.EndsWith(".out")) &&
            e.Targets.Contains("boundary_out.d_out"));
    }

    // ── End-to-end: combinational mux from ternary contassign ─────────────

    [Fact]
    public void EndToEnd_TernaryContAssign_RendersMuxAndWires()
    {
        ModuleAst topModule = ParseTop("""
            <var name="sel" dtype_id="1" dir="input"  pinIndex="1" vartype="logic"/>
            <var name="a"   dtype_id="8" dir="input"  pinIndex="2" vartype="logic"/>
            <var name="b"   dtype_id="8" dir="input"  pinIndex="3" vartype="logic"/>
            <var name="y"   dtype_id="8" dir="output" pinIndex="4" vartype="logic"/>
            <contassign dtype_id="8">
              <cond dtype_id="8">
                <varref name="sel"/>
                <varref name="a"/>
                <varref name="b"/>
              </cond>
              <varref name="y"/>
            </contassign>
            """);

        ElkBuildResult result = BuildFromModule(topModule);

        ElkNode muxNode = Assert.Single(result.Graph.Children, n => ElkNodeIds.IsMux(n.Id));
        Assert.Equal("mux_y", muxNode.Id);

        // Inputs wired
        Assert.Contains(result.Graph.Edges, e =>
            e.Sources.Contains("boundary_in.a") && e.Targets.Any(t => t.EndsWith(".in.0")));
        Assert.Contains(result.Graph.Edges, e =>
            e.Sources.Contains("boundary_in.b") && e.Targets.Any(t => t.EndsWith(".in.1")));
        Assert.Contains(result.Graph.Edges, e =>
            e.Sources.Contains("boundary_in.sel") && e.Targets.Any(t => t.EndsWith(".sel.0")));
        Assert.Contains(result.Graph.Edges, e =>
            e.Sources.Any(s => s.EndsWith(".out")) && e.Targets.Contains("boundary_out.y"));
    }

    // ── End-to-end: FF + Mux + Buffer + Splitter all together ──────────────

    [Fact]
    public void EndToEnd_MixedPrimitives_AllRenderSimultaneously()
    {
        ModuleAst topModule = ParseTop("""
            <var name="clk"    dtype_id="1" dir="input"  pinIndex="1" vartype="logic"/>
            <var name="bus"    dtype_id="8" dir="input"  pinIndex="2" vartype="logic"/>
            <var name="sel"    dtype_id="1" dir="input"  pinIndex="3" vartype="logic"/>
            <var name="a"      dtype_id="8" dir="input"  pinIndex="4" vartype="logic"/>
            <var name="b"      dtype_id="8" dir="input"  pinIndex="5" vartype="logic"/>
            <var name="muxy"   dtype_id="8" dir="output" pinIndex="6" vartype="logic"/>
            <var name="ffq"    dtype_id="8" dir="output" pinIndex="7" vartype="logic"/>
            <var name="slice"  dtype_id="2" dir="output" pinIndex="8" vartype="logic"/>
            <var name="buf_w"  dtype_id="8" dir="output" pinIndex="9" vartype="logic"/>
            <var name="q"      dtype_id="8" vartype="logic"/>
            <!-- FF -->
            <always>
              <sentree><senitem edgeType="POS"><varref name="clk"/></senitem></sentree>
              <assigndly dtype_id="8">
                <varref name="a"/>
                <varref name="q"/>
              </assigndly>
            </always>
            <!-- Mux -->
            <contassign dtype_id="8">
              <cond dtype_id="8"><varref name="sel"/><varref name="a"/><varref name="b"/></cond>
              <varref name="muxy"/>
            </contassign>
            <!-- Buffer (Q alias) -->
            <contassign dtype_id="8"><varref name="q"/><varref name="ffq"/></contassign>
            <!-- Buffer -->
            <contassign dtype_id="8"><varref name="a"/><varref name="buf_w"/></contassign>
            <!-- Splitter -->
            <contassign dtype_id="2">
              <sel dtype_id="2">
                <varref name="bus"/>
                <const name="32'h2"/>
                <const name="32'h2"/>
              </sel>
              <varref name="slice"/>
            </contassign>
            """);

        ElkBuildResult result = BuildFromModule(topModule);

        Assert.Single(result.Graph.Children, n => ElkNodeIds.IsFlipFlop(n.Id));
        Assert.Single(result.Graph.Children, n => ElkNodeIds.IsMux(n.Id));
        Assert.Single(result.Graph.Children, n => ElkNodeIds.IsSplitter(n.Id));
        // No FF/Mux/Latch/Memory leakage between primitive families
        Assert.DoesNotContain(result.Graph.Children, n => ElkNodeIds.IsLatch(n.Id));
        Assert.DoesNotContain(result.Graph.Children, n => ElkNodeIds.IsMemory(n.Id));
    }

    // ── End-to-end: memory (unpacked array) ────────────────────────────────

    [Fact]
    public void EndToEnd_UnpackedArray_RendersMemoryTile()
    {
        // Note: <unpackarraydtype> must live at netlist level — use ParseRaw helper.
        const string xml = """
            <?xml version="1.0"?>
            <verilator_xml>
              <netlist>
                <unpackarraydtype id="arr8x16" left="15" right="0"/>
                <module name="top" topModule="1">
                  <var name="mem" dtype_id="arr8x16" vartype="logic"/>
                </module>
              </netlist>
            </verilator_xml>
            """;
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, xml);
            DesignAst ast = new VerilatorXmlAstReader().Read(path);
            ElkBuildResult result = BuildFromModule(ast.TopModule!);

            ElkNode memNode = Assert.Single(result.Graph.Children, n => ElkNodeIds.IsMemory(n.Id));
            Assert.Equal("mem_mem", memNode.Id);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // ── Regression: no primitives → legacy path unchanged ──────────────────

    [Fact]
    public void NoPrimitives_NoFFOrMuxOrLatchOrMemoryNodes()
    {
        HierarchyScopePortViewModel inP = new("a", SignalDirection.Input, 8, false);
        HierarchyScopePortViewModel outP = new("y", SignalDirection.Output, 8, false);
        DesignContAssign assign = new("y", ["a"]);

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData([inP, outP], [], [], [assign]),
            compactLayout: true);

        Assert.DoesNotContain(result.Graph.Children, n => ElkNodeIds.IsFlipFlop(n.Id));
        Assert.DoesNotContain(result.Graph.Children, n => ElkNodeIds.IsMux(n.Id));
        Assert.DoesNotContain(result.Graph.Children, n => ElkNodeIds.IsLatch(n.Id));
        Assert.DoesNotContain(result.Graph.Children, n => ElkNodeIds.IsMemory(n.Id));
    }

    // ── Regression: ID prefix discriminators don't overlap ─────────────────

    [Theory]
    [InlineData("ff_q",     true,  false, false, false)]
    [InlineData("mux_y",    false, true,  false, false)]
    [InlineData("latch_q",  false, false, true,  false)]
    [InlineData("mem_arr",  false, false, false, true)]
    [InlineData("op_x",     false, false, false, false)]
    [InlineData("split_x",  false, false, false, false)]
    [InlineData("join_x",   false, false, false, false)]
    public void NodeIdPrefix_DiscriminatorsAreDisjoint(string id, bool isFF, bool isMux, bool isLatch, bool isMemory)
    {
        Assert.Equal(isFF,     ElkNodeIds.IsFlipFlop(id));
        Assert.Equal(isMux,    ElkNodeIds.IsMux(id));
        Assert.Equal(isLatch,  ElkNodeIds.IsLatch(id));
        Assert.Equal(isMemory, ElkNodeIds.IsMemory(id));
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static ModuleAst ParseTop(string moduleBody)
    {
        string xml = $"""
            <?xml version="1.0"?>
            <verilator_xml>
              <netlist>
                <module name="top" topModule="1">
                  {moduleBody}
                </module>
              </netlist>
            </verilator_xml>
            """;
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, xml);
            return new VerilatorXmlAstReader().Read(path).TopModule!;
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static ElkBuildResult BuildFromModule(ModuleAst module)
    {
        SchematicPrimitiveList primitives = SchematicDecoder.Decode(module);
        // Map ports to view-model boundary ports
        var boundaryPorts = primitives.Ports
            .Select(p => new HierarchyScopePortViewModel(p.Name, p.Direction, p.Width, false))
            .ToList();
        return new ElkGraphBuilder().Build(
            new ElkScopeData(
                boundaryPorts, [], [],
                module.ContAssigns.Select(FlattenContAssign).Where(a => a is not null).Cast<DesignContAssign>().ToList(),
                Primitives: primitives.Logic),
            compactLayout: true);
    }

    // Minimal local contassign flattener for tests — uses the legacy flattener under the hood.
    private static DesignContAssign? FlattenContAssign(ContAssignAst ca)
    {
        // Reuse a single-module flattener invocation; build a tiny ModuleAst and pull its first ContAssign.
        ModuleAst tinyModule = new(
            Name: "_t",
            IsTop: false,
            Ports: [],
            Parameters: [],
            LocalSignals: [],
            Instances: [],
            ContAssigns: [ca],
            SequentialBlocks: [],
            CombinationalBlocks: []);
        var flat = LegacyDesignFlattener.Flatten(new DesignAst([tinyModule]), fallbackTopName: "_t");
        return flat.ModuleDefinitions["_t"].ContAssigns.FirstOrDefault();
    }
}
