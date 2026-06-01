using System.IO;
using Bistable.App.Services.Routing.Elk;
using Bistable.App.ViewModels;
using Bistable.Core.Design;
using Bistable.Core.Design.Ast;
using Bistable.Core.Design.Schematic;
using Bistable.Verilator;

namespace Bistable.Snapshots;

/// <summary>
/// Phase 2 P2-5/P2-7: golden-file snapshots for the full ELK graph produced when
/// primitives (FF / Mux / Latch / Memory) are present. Locks the node IDs, port IDs,
/// port labels, and edge wiring against regressions in either the builder or the
/// decoder.
///
/// Regenerate with: BISTABLE_REGENERATE_SNAPSHOTS=1 dotnet test tests/Bistable.Snapshots
/// </summary>
[Trait("Category", "Snapshot")]
public sealed class PrimitiveElkGraphSnapshotTests
{
    // ── Register cell (FF + buffer alias) ────────────────────────────────

    [Fact]
    public void ElkSnapshot_RegisterCellWithAsyncReset_MatchesGolden()
    {
        ElkBuildResult result = BuildFromXml("""
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

        SnapshotAssert.MatchesElkGraph("elk-primitive-register-cell", result.Graph);
    }

    // ── Mux from ternary ─────────────────────────────────────────────────

    [Fact]
    public void ElkSnapshot_TernaryMux_MatchesGolden()
    {
        ElkBuildResult result = BuildFromXml("""
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

        SnapshotAssert.MatchesElkGraph("elk-primitive-ternary-mux", result.Graph);
    }

    // ── Memory tile ─────────────────────────────────────────────────────

    [Fact]
    public void ElkSnapshot_UnpackedArrayMemory_MatchesGolden()
    {
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
            SnapshotAssert.MatchesElkGraph("elk-primitive-memory-tile", result.Graph);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static ElkBuildResult BuildFromXml(string moduleBody)
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
            DesignAst ast = new VerilatorXmlAstReader().Read(path);
            return BuildFromModule(ast.TopModule!);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static ElkBuildResult BuildFromModule(ModuleAst module)
    {
        SchematicPrimitiveList primitives = SchematicDecoder.Decode(module);
        var boundaryPorts = primitives.Ports
            .Select(p => new HierarchyScopePortViewModel(p.Name, p.Direction, p.Width, false))
            .ToList();

        // Flatten contassigns for the legacy path (buffers, splitters, joiners)
        ElaboratedDesign flat = LegacyDesignFlattener.Flatten(new DesignAst([module]));
        var contAssigns = flat.ModuleDefinitions[module.Name].ContAssigns;

        return new ElkGraphBuilder().Build(
            new ElkScopeData(boundaryPorts, [], [], contAssigns, Primitives: primitives.Logic),
            compactLayout: true);
    }
}
