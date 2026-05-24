using System.IO;
using Bistable.App.Services.Routing.Elk;
using Bistable.App.ViewModels;
using Bistable.Core.Design;
using Bistable.Core.Design.Ast;
using Bistable.Core.Design.Schematic;
using Bistable.Verilator;

namespace Bistable.Snapshots;

/// <summary>
/// Phase 2 P2-11: golden-file snapshot for the full XML → AST → Decode → ElkGraphBuilder
/// pipeline when a packed-struct fan-out is involved. Locks the node IDs, port IDs,
/// port labels, and edge wiring so future refactors of the fan-out path are caught.
///
/// Regenerate with: BISTABLE_REGENERATE_SNAPSHOTS=1 dotnet test tests/Bistable.Snapshots
/// </summary>
[Trait("Category", "Snapshot")]
public sealed class StructFanOutSnapshotTests
{
    [Fact]
    public void ElkSnapshot_StructFanOutWithTwoFields_MatchesGolden()
    {
        const string xml = """
            <?xml version="1.0"?>
            <verilator_xml>
              <netlist>
                <module name="top" topModule="1">
                  <var name="ctrl" dtype_id="10" vartype="ctrl_t"/>
                  <instance name="alu_i" defName="alu">
                    <port name="ops" direction="in" portIndex="1">
                      <sel>
                        <varref name="ctrl"/>
                        <const name="32'h1"/>
                        <const name="32'h2"/>
                      </sel>
                    </port>
                    <port name="we" direction="in" portIndex="2">
                      <sel>
                        <varref name="ctrl"/>
                        <const name="32'h0"/>
                        <const name="32'h1"/>
                      </sel>
                    </port>
                  </instance>
                </module>
                <typetable>
                  <structdtype id="10" name="pkg::ctrl_t">
                    <memberdtype id="20" name="ops" sub_dtype_id="200"/>
                    <memberdtype id="21" name="we"  sub_dtype_id="100"/>
                  </structdtype>
                  <basicdtype id="200" name="logic" left="1" right="0"/>
                  <basicdtype id="100" name="logic"/>
                </typetable>
              </netlist>
            </verilator_xml>
            """;

        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, xml);
            DesignAst ast = new VerilatorXmlAstReader().Read(path);
            ModuleAst topModule = ast.TopModule!;
            SchematicPrimitiveList primitives = SchematicDecoder.Decode(topModule);

            // Build with a single alu_i child whose port pins match the struct slices
            HierarchyScopeInstanceViewModel alu = new(
                hierarchyPath: "top.alu_i",
                instanceName: "alu_i",
                moduleName: "alu",
                inputCount: 2,
                outputCount: 0,
                exactSignalCount: 0,
                descendantSignalCount: 0,
                portConnections: [
                    new HierarchyScopeInstancePortConnectionViewModel("ops", "ctrl", isInput: true, width: 2),
                    new HierarchyScopeInstancePortConnectionViewModel("we",  "ctrl", isInput: true, width: 1),
                ]);

            // No legacy contassigns — only the struct fan-out drives the consumers
            ElkBuildResult result = new ElkGraphBuilder().Build(
                new ElkScopeData(
                    BoundaryPorts: [],
                    ChildScopes: [alu],
                    LocalSignals: [],
                    ContAssigns: [],
                    Primitives: primitives.Logic),
                compactLayout: true);

            SnapshotAssert.MatchesElkGraph("elk-struct-fanout-two-fields", result.Graph);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
