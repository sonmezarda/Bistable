using System.IO;
using Bistable.Core.Design.Ast;
using Bistable.Verilator;

namespace Bistable.Tests.Ast;

internal static class AstReaderTestHelper
{
    /// <summary>Wraps an inline XML body in a minimal valid Verilator netlist and parses it.</summary>
    internal static DesignAst ParseInline(string moduleBody, string moduleName = "top")
    {
        string xml = $"""
            <?xml version="1.0"?>
            <verilator_xml>
              <netlist>
                <module name="{moduleName}" topModule="1">
                  {moduleBody}
                </module>
              </netlist>
            </verilator_xml>
            """;

        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, xml);
            return new VerilatorXmlAstReader().Read(path);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
