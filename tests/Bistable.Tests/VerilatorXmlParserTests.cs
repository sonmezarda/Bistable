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
}
