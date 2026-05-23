using System.Diagnostics;
using System.IO;
using System.Text;
using Bistable.Core.Design.Ast;
using Bistable.Verilator;

namespace Bistable.Tests.Ast;

[Trait("Speed", "Slow")]
public sealed class AstPerformanceTests
{
    // Generates a synthetic XML with many modules, always blocks, and nested expressions
    // to verify there is no accidental O(N²) behaviour in the reader. The arnicomp real-world
    // XML is tiny (~400 lines); this synthetic design is intentionally larger.
    [Fact]
    public void AstReader_LargeDesign_ParsesUnder500ms()
    {
        string xml = BuildSyntheticXml(moduleCount: 20, alwaysPerModule: 8, nestDepth: 5);
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, xml);
            var reader = new VerilatorXmlAstReader();

            // Warm up (JIT).
            reader.Read(path);

            var sw = Stopwatch.StartNew();
            DesignAst ast = reader.Read(path);
            sw.Stop();

            Assert.True(sw.ElapsedMilliseconds < 500,
                $"AST parse took {sw.ElapsedMilliseconds} ms, expected < 500 ms. " +
                "Check for accidental O(N²) in expression traversal.");
            Assert.Equal(20, ast.Modules.Count);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string BuildSyntheticXml(int moduleCount, int alwaysPerModule, int nestDepth)
    {
        var sb = new StringBuilder();
        sb.AppendLine("""<?xml version="1.0"?><verilator_xml><netlist>""");

        for (int m = 0; m < moduleCount; m++)
        {
            bool isTop = m == 0;
            sb.Append($"""<module name="mod_{m}" """);
            if (isTop) sb.Append("""topModule="1" """);
            sb.AppendLine(">");

            // signals
            sb.AppendLine("""<var name="clk" dtype_id="1" dir="input" pinIndex="1" vartype="logic"/>""");
            sb.AppendLine("""<var name="rst_n" dtype_id="1" dir="input" pinIndex="2" vartype="logic"/>""");
            for (int i = 0; i < alwaysPerModule; i++)
                sb.AppendLine($"""<var name="q_{i}" dtype_id="8" vartype="logic"/>""");

            // always blocks with nested cond expressions
            for (int a = 0; a < alwaysPerModule; a++)
            {
                sb.AppendLine("""<always><sentree><senitem edgeType="POS"><varref name="clk"/></senitem></sentree><begin>""");
                sb.Append("""<assigndly dtype_id="8">""");
                sb.Append(BuildNestedCond(nestDepth));
                sb.Append($"""<varref name="q_{a}"/>""");
                sb.AppendLine("</assigndly></begin></always>");
            }

            sb.AppendLine("</module>");
        }

        sb.AppendLine("</netlist></verilator_xml>");
        return sb.ToString();
    }

    private static string BuildNestedCond(int depth)
    {
        if (depth <= 0) return """<varref name="clk"/>""";
        return $"""<cond><varref name="rst_n"/>{BuildNestedCond(depth - 1)}<const name="8'h0"/></cond>""";
    }
}
