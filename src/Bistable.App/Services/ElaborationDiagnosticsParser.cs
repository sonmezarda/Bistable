using System.Text.RegularExpressions;

namespace Bistable.App.Services;

public static partial class ElaborationDiagnosticsParser
{
    [GeneratedRegex(
        @"^%(?<severity>Error|Warning)(?:-(?<code>[A-Za-z0-9_]+))?:\s+(?<file>.+?):(?<line>\d+):(?<column>\d+):\s*(?<message>.*)$",
        RegexOptions.CultureInvariant)]
    private static partial Regex DiagnosticPattern();

    public static IReadOnlyList<ElaborationDiagnostic> Parse(string stderr, string projectDirectory)
    {
        if (string.IsNullOrWhiteSpace(stderr)) return [];
        List<ElaborationDiagnostic> diagnostics = [];
        foreach (string rawLine in stderr.ReplaceLineEndings("\n").Split('\n'))
        {
            Match match = DiagnosticPattern().Match(rawLine.Trim());
            if (!match.Success) continue;
            string file = match.Groups["file"].Value;
            string fullPath = Path.IsPathRooted(file) ? Path.GetFullPath(file) : Path.GetFullPath(file, projectDirectory);
            diagnostics.Add(new ElaborationDiagnostic(
                string.Equals(match.Groups["severity"].Value, "Error", StringComparison.OrdinalIgnoreCase)
                    ? ElaborationDiagnosticSeverity.Error
                    : ElaborationDiagnosticSeverity.Warning,
                match.Groups["code"].Success ? match.Groups["code"].Value : null,
                match.Groups["message"].Value.Trim(),
                fullPath,
                int.Parse(match.Groups["line"].Value, System.Globalization.CultureInfo.InvariantCulture),
                int.Parse(match.Groups["column"].Value, System.Globalization.CultureInfo.InvariantCulture),
                rawLine.Trim()));
        }
        return diagnostics;
    }
}

public enum ElaborationDiagnosticSeverity
{
    Warning,
    Error
}

public sealed record ElaborationDiagnostic(
    ElaborationDiagnosticSeverity Severity,
    string? Code,
    string Message,
    string FilePath,
    int Line,
    int Column,
    string RawText)
{
    public string Location => $"{Path.GetFileName(FilePath)}:{Line}:{Column}";
    public string DisplayText => $"{Location}  {Message}";
}
