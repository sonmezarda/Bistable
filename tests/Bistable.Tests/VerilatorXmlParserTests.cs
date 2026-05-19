using Bistable.Core.Design;
using Bistable.Verilator;

namespace Bistable.Tests;

public sealed class VerilatorXmlParserTests
{
    [Fact]
    public void ParsesTopLevelPortsAndResolvedWidths()
    {
        string xmlPath = Path.Combine(Path.GetTempPath(), $"bistable-{Guid.NewGuid():N}.xml");
        File.WriteAllText(xmlPath, """
        <verilator_xml>
          <netlist>
            <module name="alu" topModule="1">
              <var name="clk" dtype_id="1" dir="input" pinIndex="1" vartype="logic" />
              <var name="a" dtype_id="2" dir="input" pinIndex="2" vartype="logic" />
              <var name="y" dtype_id="3" dir="output" pinIndex="3" vartype="logic" />
              <var name="W" dtype_id="4" param="true">
                <const name="32&apos;sh8" dtype_id="4" />
              </var>
            </module>
            <typetable>
              <basicdtype id="1" name="logic" />
              <basicdtype id="2" name="logic" left="7" right="0" />
              <basicdtype id="3" name="logic" left="8" right="0" />
              <basicdtype id="4" name="int" left="31" right="0" signed="true" />
            </typetable>
          </netlist>
        </verilator_xml>
        """);

        try
        {
            ModuleMetadata metadata = new VerilatorXmlParser().Parse(xmlPath);

            Assert.Equal("alu", metadata.Name);
            Assert.Equal(3, metadata.Ports.Count);
            Assert.Equal(1, metadata.Ports[0].Width);
            Assert.Equal(8, metadata.Ports[1].Width);
            Assert.Equal(9, metadata.Ports[2].Width);
            Assert.Equal(SignalDirection.Output, metadata.Ports[2].Direction);
            Assert.Equal("W", metadata.Parameters.Single().Name);
        }
        finally
        {
            File.Delete(xmlPath);
        }
    }

    [Fact]
    public void ParsesHierarchyTreeFromCells()
    {
        string xmlPath = Path.Combine(Path.GetTempPath(), $"bistable-hier-{Guid.NewGuid():N}.xml");
        File.WriteAllText(xmlPath, """
        <verilator_xml>
          <cells>
            <cell name="system_top" submodname="system_top" hier="system_top" />
            <cell name="u_core" submodname="core_cluster" hier="system_top.u_core" />
            <cell name="u_logic" submodname="logic_unit" hier="system_top.u_core.u_logic" />
            <cell name="u_status" submodname="status_reg" hier="system_top.u_core.u_status" />
          </cells>
          <netlist>
            <module name="system_top" topModule="1">
              <var name="clk" dtype_id="1" dir="input" pinIndex="1" vartype="logic" />
              <var name="valid" dtype_id="1" dir="output" pinIndex="2" vartype="logic" />
            </module>
            <typetable>
              <basicdtype id="1" name="logic" />
            </typetable>
          </netlist>
        </verilator_xml>
        """);

        try
        {
            ElaboratedDesign design = new VerilatorXmlParser().ParseDesign(xmlPath);

            Assert.Equal("system_top", design.HierarchyRoot.InstanceName);
            DesignHierarchyNode core = Assert.Single(design.HierarchyRoot.Children);
            Assert.Equal("u_core", core.InstanceName);
            Assert.Equal("core_cluster", core.ModuleName);
            Assert.Equal(2, core.Children.Count);
            Assert.Contains(core.Children, static child => child.InstanceName == "u_logic");
            Assert.Contains(core.Children, static child => child.InstanceName == "u_status");
        }
        finally
        {
            File.Delete(xmlPath);
        }
    }
}
