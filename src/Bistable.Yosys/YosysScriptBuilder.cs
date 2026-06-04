using System.Text;
using Bistable.Core.Projects;

namespace Bistable.Yosys;

/// <summary>
/// Phase 6 P6-3: emits a default Yosys synthesis script from a project's
/// <see cref="SynthesisConfiguration"/>. Users with hand-written scripts can
/// point <c>SynthesisConfiguration.Script</c> at their own file; this builder
/// is the fallback for the common "I just want to see gates" case.
///
/// Output stages: <c>read_verilog</c> → <c>hierarchy -top</c> → <c>proc</c>
/// → <c>opt</c> → <c>fsm</c> → <c>opt</c> → <c>memory</c> → <c>opt</c> →
/// (optional <c>flatten</c>) → (<c>techmap</c> when <c>genericCells</c> is true)
/// → <c>opt</c> → <c>write_json</c>.
///
/// `techmap` is what lowers high-level cells into the generic `$_AND_`,
/// `$_OR_`, `$_DFF_*` etc. that the gate-level renderer expects.
/// </summary>
public static class YosysScriptBuilder
{
    public static string Build(
        ProjectConfiguration project,
        SynthesisConfiguration synthesis,
        string projectDirectory)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(synthesis);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectDirectory);

        string top = synthesis.TopModule ?? project.TopModule;
        if (string.IsNullOrWhiteSpace(top))
        {
            throw new InvalidOperationException("Synthesis script needs a top module name.");
        }

        StringBuilder sb = new();

        // read_verilog -sv <sources>
        sb.Append("read_verilog -sv");
        foreach (string source in project.Sources)
        {
            sb.Append(' ');
            sb.Append(EscapePath(ResolvePath(projectDirectory, source)));
        }
        sb.AppendLine();

        // Add `read_verilog +/proc/null` style flags from project Defines/Parameters?
        // Defer until a real sample demands it.

        sb.AppendLine($"hierarchy -check -top {top}");
        sb.AppendLine("proc");
        sb.AppendLine("opt");
        sb.AppendLine("fsm");
        sb.AppendLine("opt");
        sb.AppendLine("memory");
        sb.AppendLine("opt");

        if (synthesis.Flatten)
        {
            sb.AppendLine("flatten");
            sb.AppendLine("opt");
        }

        if (synthesis.GenericCells)
        {
            // Lower to the generic gate library so the schematic renderer can
            // dispatch on `$_AND_` / `$_DFF_*` / etc.
            sb.AppendLine("techmap");
            sb.AppendLine("opt");
        }

        string outputJson = Path.IsPathRooted(synthesis.OutputJson)
            ? synthesis.OutputJson
            : Path.Combine(projectDirectory, synthesis.OutputJson);
        Directory.CreateDirectory(Path.GetDirectoryName(outputJson) ?? projectDirectory);
        sb.AppendLine($"write_json {EscapePath(outputJson)}");

        return sb.ToString();
    }

    private static string ResolvePath(string projectDirectory, string maybeRelative)
        => Path.IsPathRooted(maybeRelative)
            ? maybeRelative
            : Path.GetFullPath(Path.Combine(projectDirectory, maybeRelative));

    private static string EscapePath(string path) =>
        path.Contains(' ', StringComparison.Ordinal) ? '"' + path + '"' : path;
}
