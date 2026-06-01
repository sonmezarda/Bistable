using System.IO;
using Bistable.Core.Design;
using Bistable.Verilator;

namespace Bistable.Regression;

// Bug-locking tests for VerilatorXmlParser. Each test ships with the fix it protects.
//
// Reference: docs/PHASES/PHASE-0.md Section 2.
[Trait("Category", "Regression")]
public sealed class VerilatorXmlParserRegressionTests
{
    // 2026-05-23: packed-struct field connections (e.g. `.ops(control_pins.ops)`) are
    // serialized by Verilator as <port><sel><varref name="control_pins"/>...</sel></port>
    // — the parser had only looked at the direct <varref> child and so reported "?"
    // for the signal name, dropping the wire. The fix added a fallback that descends
    // into <sel> and uses the base varref name. This test guards that fallback.
    [Fact]
    public void InstancePortConnection_WithSelWrappedVarref_ResolvesToBaseSignalName()
    {
        const string xml = """
            <?xml version="1.0"?>
            <verilator_xml>
              <netlist>
                <module name="top" topModule="1">
                  <var name="clk" dtype_id="bit" dir="input" pinIndex="1" vartype="logic"/>
                  <var name="control_pins" dtype_id="struct" vartype="logic"/>
                  <instance name="alu_i" defName="alu" origName="alu_i">
                    <port name="ops" direction="in" portIndex="1">
                      <sel>
                        <varref name="control_pins" dtype_id="struct"/>
                        <const name="32'h14" dtype_id="bit32"/>
                        <const name="32'h2" dtype_id="bit32"/>
                      </sel>
                    </port>
                  </instance>
                </module>
                <module name="alu">
                  <var name="ops" dtype_id="bit2" dir="input" pinIndex="1" vartype="logic"/>
                </module>
              </netlist>
            </verilator_xml>
            """;

        string tempPath = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempPath, xml);
            ElaboratedDesign design = VerilatorXmlParser.ParseDesign(tempPath);

            DesignModuleDefinition top = design.ModuleDefinitions["top"];
            DesignInstanceDefinition aluInstance = Assert.Single(top.Instances);
            DesignInstancePortConnection opsPort = Assert.Single(aluInstance.PortConnections);

            // The signal name must be the base struct varref, not "?" or null.
            Assert.Equal("control_pins", opsPort.SignalName);
            Assert.Equal("ops", opsPort.PortName);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }
}
