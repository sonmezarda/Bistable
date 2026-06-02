using Bistable.Verilator;

namespace Bistable.Tests.Ast;

public sealed class VerilatorXmlAstDiagnosticsTests
{
    [Fact]
    public void Read_UnknownExpressionFallback_RecordsDiagnostic()
    {
        VerilatorXmlAstReader reader = ReadInline("""
            <var name="out" dir="output" dtype_id="1"/>
            <contassign dtype_id="1">
              <mysteryexpr dtype_id="1">
                <varref name="a"/>
              </mysteryexpr>
              <varref name="out"/>
            </contassign>
            """);

        VerilatorXmlAstDiagnostic diagnostic = Assert.Single(reader.LastDiagnostics);
        Assert.Equal("top", diagnostic.ModuleName);
        Assert.Equal("mysteryexpr", diagnostic.ElementName);
        Assert.Equal(VerilatorXmlAstDiagnosticKind.UnknownExpressionFallback, diagnostic.Kind);
        Assert.Contains("zero constant", diagnostic.Reason);
    }

    [Fact]
    public void Read_UnknownStatementFallback_RecordsDiagnostic()
    {
        VerilatorXmlAstReader reader = ReadInline("""
            <always>
              <mysterystmt>
                <varref name="a"/>
              </mysterystmt>
            </always>
            """);

        VerilatorXmlAstDiagnostic diagnostic = Assert.Single(reader.LastDiagnostics);
        Assert.Equal("top", diagnostic.ModuleName);
        Assert.Equal("mysterystmt", diagnostic.ElementName);
        Assert.Equal(VerilatorXmlAstDiagnosticKind.UnknownStatementFallback, diagnostic.Kind);
    }

    private static VerilatorXmlAstReader ReadInline(string moduleBody)
    {
        string xml = $"""
            <?xml version="1.0"?>
            <verilator_xml>
              <netlist>
                <typetable>
                  <basicdtype id="1"/>
                </typetable>
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
            VerilatorXmlAstReader reader = new();
            reader.Read(path);
            return reader;
        }
        finally
        {
            File.Delete(path);
        }
    }
}
