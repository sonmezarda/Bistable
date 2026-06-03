using Bistable.Core.Design.Ast;

namespace Bistable.Tests.Ast;

public sealed class ModuleAstTests
{
    [Fact]
    public void DesignAst_TopModule_HasIsTopTrue()
    {
        DesignAst ast = AstReaderTestHelper.ParseInline(string.Empty);
        Assert.NotNull(ast.TopModule);
        Assert.True(ast.TopModule.IsTop);
    }

    [Fact]
    public void ModuleAst_Ports_ParsedInPinOrder()
    {
        DesignAst ast = AstReaderTestHelper.ParseInline("""
            <var name="clk"  dtype_id="1" dir="input"  pinIndex="1" vartype="logic"/>
            <var name="out"  dtype_id="8" dir="output" pinIndex="2" vartype="logic"/>
            <var name="data" dtype_id="8" dir="input"  pinIndex="3" vartype="logic"/>
            """);

        ModuleAst top = ast.TopModule!;
        Assert.Equal(3, top.Ports.Count);
        Assert.Equal("clk",  top.Ports[0].Name);
        Assert.Equal("out",  top.Ports[1].Name);
        Assert.Equal("data", top.Ports[2].Name);
    }

    [Fact]
    public void ModuleAst_OriginalName_ParsedFromModuleOrigName()
    {
        string xml = """
            <?xml version="1.0"?>
            <verilator_xml>
              <netlist>
                <module name="reg_cell__W4" origName="reg_cell" topModule="1" />
              </netlist>
            </verilator_xml>
            """;
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, xml);
            DesignAst ast = new Bistable.Verilator.VerilatorXmlAstReader().Read(path);

            Assert.Equal("reg_cell__W4", ast.TopModule!.Name);
            Assert.Equal("reg_cell", ast.TopModule.OriginalName);
            Assert.Equal("reg_cell", ast.TopModule.SourceName);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ModuleAst_LocalSignals_ExcludePortsAndParameters()
    {
        DesignAst ast = AstReaderTestHelper.ParseInline("""
            <var name="clk"  dtype_id="1" dir="input" pinIndex="1" vartype="logic"/>
            <var name="WIDTH" dtype_id="32" param="true" vartype="parameter"/>
            <var name="OPCODE" dtype_id="8" localparam="true" vartype="logic"/>
            <var name="local_w" dtype_id="8" vartype="logic"/>
            """);

        ModuleAst top = ast.TopModule!;
        Assert.Single(top.LocalSignals);
        Assert.Equal("local_w", top.LocalSignals[0].Name);
    }

    [Fact]
    public void ModuleAst_Instance_ParsedWithPortConnections()
    {
        DesignAst ast = AstReaderTestHelper.ParseInline("""
            <instance name="child_i" defName="child_mod" origName="child_i">
              <port name="clk" direction="in" portIndex="1"><varref name="clk"/></port>
              <port name="out" direction="out" portIndex="2"><varref name="result"/></port>
            </instance>
            """);

        InstanceDecl inst = Assert.Single(ast.TopModule!.Instances);
        Assert.Equal("child_i",   inst.InstanceName);
        Assert.Equal("child_mod", inst.ModuleName);
        Assert.Equal(2, inst.PortConnections.Count);
        Assert.Equal("clk",    inst.PortConnections[0].PortName);
        Assert.Equal("clk",    inst.PortConnections[0].SignalName);
        Assert.Equal("result", inst.PortConnections[1].SignalName);
    }

    [Fact]
    public void ModuleAst_SelWrappedPortConnection_ResolvesToBaseSignal()
    {
        // Regression guard: packed-struct port connections use <sel><varref/></sel>
        DesignAst ast = AstReaderTestHelper.ParseInline("""
            <instance name="alu_i" defName="alu" origName="alu_i">
              <port name="ops" direction="in" portIndex="1">
                <sel>
                  <varref name="control_pins" dtype_id="struct"/>
                  <const name="32'h14"/>
                  <const name="32'h2"/>
                </sel>
              </port>
            </instance>
            """);

        InstanceDecl inst = Assert.Single(ast.TopModule!.Instances);
        PortConnectionDecl ops = Assert.Single(inst.PortConnections);
        Assert.Equal("control_pins", ops.SignalName);
    }

    [Fact]
    public void DesignAst_MultipleModules_AllParsed()
    {
        const string xml = """
            <?xml version="1.0"?>
            <verilator_xml>
              <netlist>
                <module name="top" topModule="1"/>
                <module name="child"/>
              </netlist>
            </verilator_xml>
            """;

        string path = System.IO.Path.GetTempFileName();
        try
        {
            System.IO.File.WriteAllText(path, xml);
            DesignAst ast = new Bistable.Verilator.VerilatorXmlAstReader().Read(path);
            Assert.Equal(2, ast.Modules.Count);
            Assert.Single(ast.Modules, m => m.IsTop);
        }
        finally
        {
            System.IO.File.Delete(path);
        }
    }
}
