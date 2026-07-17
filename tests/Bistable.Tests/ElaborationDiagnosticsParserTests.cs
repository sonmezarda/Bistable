using Bistable.Engine;

namespace Bistable.Tests;

public sealed class ElaborationDiagnosticsParserTests
{
    [Fact]
    public void Parse_ErrorAndWarning_ProducesClickableLocations()
    {
        string root = Path.Combine(Path.GetTempPath(), "bistable-diagnostics");
        string stderr = """
            %Error: rtl/top.sv:12:7: syntax error, unexpected always_comb
            %Warning-WIDTH: rtl/alu.sv:33:4: Operator ADD expects 32 bits
            %Error: Exiting due to 1 error(s)
            """;

        IReadOnlyList<ElaborationDiagnostic> diagnostics = ElaborationDiagnosticsParser.Parse(stderr, root);

        Assert.Collection(diagnostics,
            error =>
            {
                Assert.Equal(ElaborationDiagnosticSeverity.Error, error.Severity);
                Assert.Equal(12, error.Line);
                Assert.Equal(7, error.Column);
                Assert.EndsWith(Path.Combine("rtl", "top.sv"), error.FilePath);
            },
            warning =>
            {
                Assert.Equal(ElaborationDiagnosticSeverity.Warning, warning.Severity);
                Assert.Equal("WIDTH", warning.Code);
                Assert.Equal("alu.sv:33:4", warning.Location);
            });
    }
}
