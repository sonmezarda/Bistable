using System.Diagnostics;
using System.Text;
using Bistable.Core.Design;
using Bistable.Core.Projects;

namespace Bistable.Verilator;

public sealed class SimulationWorkerBuilder(string verilatorExecutablePath = "verilator")
{
    private readonly VerilatorTool _verilator = new(verilatorExecutablePath);

    public async Task<SimulationWorkerBuildResult> BuildAsync(
        ProjectConfiguration configuration,
        ModuleMetadata metadata,
        string projectDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectDirectory);

        IReadOnlyList<SignalPort> unsupportedPorts = metadata.Ports.Where(static port => port.Width > 64).ToArray();
        if (unsupportedPorts.Count > 0)
        {
            string names = string.Join(", ", unsupportedPorts.Select(static port => $"{port.Name}[{port.Width}]"));
            throw new NotSupportedException($"Native worker currently supports ports up to 64 bits. Unsupported ports: {names}");
        }

        string buildDirectory = Path.Combine(projectDirectory, ".bistable", "worker", configuration.TopModule);
        Directory.CreateDirectory(buildDirectory);

        string wrapperPath = Path.Combine(buildDirectory, "bistable_worker.cpp");
        WorkerGenerationOptions options = new(
            configuration.Clocks.FirstOrDefault()?.Name,
            configuration.Resets.FirstOrDefault()?.Name,
            configuration.Resets.FirstOrDefault()?.ActiveLevel ?? 0);
        await File.WriteAllTextAsync(wrapperPath, GenerateWorkerSource(configuration.TopModule, metadata, options), cancellationToken);

        List<string> arguments =
        [
            "--cc",
            "--exe",
            "--build",
            "--top-module",
            configuration.TopModule,
            "--Mdir",
            buildDirectory,
            "-o",
            "bistable-worker"
        ];

        foreach (string includeDir in configuration.IncludeDirs)
        {
            arguments.Add("-I" + ResolvePath(projectDirectory, includeDir));
        }

        foreach (KeyValuePair<string, string> define in configuration.Defines)
        {
            arguments.Add($"+define+{define.Key}={define.Value}");
        }

        foreach (KeyValuePair<string, string> parameter in configuration.Parameters)
        {
            arguments.Add("-G" + parameter.Key + "=" + parameter.Value);
        }

        arguments.AddRange(configuration.VerilatorOptions);
        arguments.AddRange(configuration.Sources.Select(source => ResolvePath(projectDirectory, source)));
        arguments.Add(wrapperPath);

        await _verilator.RunAsync(arguments, projectDirectory, cancellationToken);

        string executablePath = Path.Combine(buildDirectory, "bistable-worker");
        if (!File.Exists(executablePath))
        {
            throw new InvalidOperationException($"Worker build completed but executable was not found: {executablePath}");
        }

        return new SimulationWorkerBuildResult(executablePath, buildDirectory);
    }

    private static string GenerateWorkerSource(string topModule, ModuleMetadata metadata, WorkerGenerationOptions options)
    {
        string modelType = "V" + SanitizeIdentifier(topModule);
        StringBuilder builder = new();
        builder.AppendLine("#include <cstdint>");
        builder.AppendLine("#include <cstdlib>");
        builder.AppendLine("#include <iostream>");
        builder.AppendLine("#include <memory>");
        builder.AppendLine("#include <regex>");
        builder.AppendLine("#include <sstream>");
        builder.AppendLine("#include <string>");
        builder.AppendLine("#include \"verilated.h\"");
        builder.AppendLine($"#include \"{modelType}.h\"");
        builder.AppendLine();
        builder.AppendLine("namespace {");
        builder.AppendLine("std::uint64_t parse_u64(const std::string& text) {");
        builder.AppendLine("    std::size_t index = 0;");
        builder.AppendLine("    int base = 10;");
        builder.AppendLine("    if (text.rfind(\"0x\", 0) == 0 || text.rfind(\"0X\", 0) == 0) { index = 2; base = 16; }");
        builder.AppendLine("    else if (text.rfind(\"0b\", 0) == 0 || text.rfind(\"0B\", 0) == 0) {");
        builder.AppendLine("        std::uint64_t value = 0;");
        builder.AppendLine("        for (std::size_t i = 2; i < text.size(); ++i) {");
        builder.AppendLine("            if (text[i] != '0' && text[i] != '1') throw std::invalid_argument(\"invalid binary value\");");
        builder.AppendLine("            value = (value << 1U) | static_cast<std::uint64_t>(text[i] == '1');");
        builder.AppendLine("        }");
        builder.AppendLine("        return value;");
        builder.AppendLine("    }");
        builder.AppendLine("    return std::stoull(text.substr(index), nullptr, base);");
        builder.AppendLine("}");
        builder.AppendLine();
        builder.AppendLine("std::string json_escape(const std::string& value) {");
        builder.AppendLine("    std::ostringstream out;");
        builder.AppendLine("    for (char c : value) {");
        builder.AppendLine("        if (c == '\\\\' || c == '\"') out << '\\\\';");
        builder.AppendLine("        out << c;");
        builder.AppendLine("    }");
        builder.AppendLine("    return out.str();");
        builder.AppendLine("}");
        builder.AppendLine();
        builder.AppendLine("std::string get_string(const std::string& json, const std::string& key) {");
        builder.AppendLine("    std::regex pattern(\"\\\\\\\"\" + key + \"\\\\\\\"\\\\s*:\\\\s*\\\\\\\"([^\\\\\\\"]*)\\\\\\\"\");");
        builder.AppendLine("    std::smatch match;");
        builder.AppendLine("    return std::regex_search(json, match, pattern) ? match[1].str() : std::string{};");
        builder.AppendLine("}");
        builder.AppendLine();
        builder.AppendLine("std::uint64_t get_u64(const std::string& json, const std::string& key, std::uint64_t fallback) {");
        builder.AppendLine("    std::regex pattern(\"\\\\\\\"\" + key + \"\\\\\\\"\\\\s*:\\\\s*([0-9]+)\");");
        builder.AppendLine("    std::smatch match;");
        builder.AppendLine("    return std::regex_search(json, match, pattern) ? std::stoull(match[1].str()) : fallback;");
        builder.AppendLine("}");
        builder.AppendLine();
        AppendDriveClockFunction(builder, modelType, metadata);
        AppendApplyResetFunction(builder, modelType, metadata, options);
        builder.AppendLine();
        builder.AppendLine("void write_snapshot(const " + modelType + "& model, std::uint64_t time) {");
        builder.AppendLine("    std::cout << \"{\\\"time\\\":\" << time << \",\\\"signals\\\":[\";");
        bool first = true;
        foreach (SignalPort port in metadata.Outputs)
        {
            string separator = first ? string.Empty : ",";
            first = false;
            builder.AppendLine($"    std::cout << \"{separator}{{\\\"signal\\\":\\\"{port.Name}\\\",\\\"value\\\":\\\"\" << static_cast<unsigned long long>(model.{port.Name}) << \"\\\",\\\"time\\\":\" << time << \"}}\";");
        }

        builder.AppendLine("    std::cout << \"]}\" << std::endl;");
        builder.AppendLine("}");
        builder.AppendLine("}");
        builder.AppendLine();
        builder.AppendLine("int main(int argc, char** argv) {");
        builder.AppendLine("    Verilated::commandArgs(argc, argv);");
        builder.AppendLine($"    auto model = std::make_unique<{modelType}>();");
        builder.AppendLine("    std::uint64_t time = 0;");
        builder.AppendLine("    std::string line;");
        builder.AppendLine("    while (std::getline(std::cin, line)) {");
        builder.AppendLine("        try {");
        builder.AppendLine("            const std::string type = get_string(line, \"type\");");
        builder.AppendLine("            if (type == \"setInput\") {");
        builder.AppendLine("                const std::string signal = get_string(line, \"signal\");");
        builder.AppendLine("                const std::uint64_t value = parse_u64(get_string(line, \"value\"));");
        foreach (SignalPort port in metadata.Inputs)
        {
            builder.AppendLine($"                if (signal == \"{port.Name}\") model->{port.Name} = value;");
        }

        builder.AppendLine("                model->eval();");
        builder.AppendLine("                write_snapshot(*model, time);");
        builder.AppendLine("            } else if (type == \"eval\" || type == \"getSnapshot\") {");
        builder.AppendLine("                model->eval();");
        builder.AppendLine("                write_snapshot(*model, time);");
        builder.AppendLine("            } else if (type == \"tick\") {");
        builder.AppendLine("                const std::string clock = get_string(line, \"signal\");");
        builder.AppendLine("                drive_clock(*model, clock);");
        builder.AppendLine("                ++time;");
        builder.AppendLine("                write_snapshot(*model, time);");
        builder.AppendLine("            } else if (type == \"runCycles\") {");
        builder.AppendLine("                const std::uint64_t cycles = get_u64(line, \"cycles\", 1);");
        builder.AppendLine("                const std::string clock = get_string(line, \"signal\");");
        builder.AppendLine("                for (std::uint64_t i = 0; i < cycles; ++i) {");
        builder.AppendLine("                    if (!clock.empty()) {");
        builder.AppendLine("                        drive_clock(*model, clock);");
        builder.AppendLine("                    }");
        builder.AppendLine("                    ++time;");
        builder.AppendLine("                }");
        builder.AppendLine("                write_snapshot(*model, time);");
        builder.AppendLine("            } else if (type == \"reset\") {");
        builder.AppendLine("                time = 0;");
        builder.AppendLine("                model = std::make_unique<" + modelType + ">();");
        builder.AppendLine("                apply_reset(*model);");
        builder.AppendLine("                write_snapshot(*model, time);");
        builder.AppendLine("            } else if (type == \"pause\") {");
        builder.AppendLine("                write_snapshot(*model, time);");
        builder.AppendLine("            }");
        builder.AppendLine("        } catch (const std::exception& ex) {");
        builder.AppendLine("            std::cout << \"{\\\"time\\\":\" << time << \",\\\"signals\\\":[],\\\"error\\\":\\\"\" << json_escape(ex.what()) << \"\\\"}\" << std::endl;");
        builder.AppendLine("        }");
        builder.AppendLine("    }");
        builder.AppendLine("    return 0;");
        builder.AppendLine("}");

        return builder.ToString();
    }

    private static void AppendDriveClockFunction(StringBuilder builder, string modelType, ModuleMetadata metadata)
    {
        builder.AppendLine("void drive_clock(" + modelType + "& model, const std::string& clock) {");
        builder.AppendLine("    if (clock.empty()) { model.eval(); return; }");
        foreach (SignalPort port in metadata.Inputs.Where(static port => port.Width == 1))
        {
            builder.AppendLine($"    if (clock == \"{port.Name}\") {{ model.{port.Name} = 0; model.eval(); model.{port.Name} = 1; model.eval(); model.{port.Name} = 0; model.eval(); return; }}");
        }

        builder.AppendLine("    model.eval();");
        builder.AppendLine("}");
        builder.AppendLine();
    }

    private static void AppendApplyResetFunction(
        StringBuilder builder,
        string modelType,
        ModuleMetadata metadata,
        WorkerGenerationOptions options)
    {
        string? reset = options.ResetSignal is not null && metadata.Inputs.Any(port => port.Name == options.ResetSignal && port.Width == 1)
            ? options.ResetSignal
            : null;
        string? clock = options.DefaultClock is not null && metadata.Inputs.Any(port => port.Name == options.DefaultClock && port.Width == 1)
            ? options.DefaultClock
            : null;

        builder.AppendLine("void apply_reset(" + modelType + "& model) {");
        if (reset is null)
        {
            builder.AppendLine("    model.eval();");
            builder.AppendLine("}");
            builder.AppendLine();
            return;
        }

        int active = options.ResetActiveLevel == 0 ? 0 : 1;
        int inactive = active == 0 ? 1 : 0;
        builder.AppendLine($"    model.{reset} = {active};");
        builder.AppendLine("    model.eval();");
        if (clock is not null)
        {
            builder.AppendLine($"    model.{clock} = 0; model.eval(); model.{clock} = 1; model.eval(); model.{clock} = 0; model.eval();");
        }

        builder.AppendLine($"    model.{reset} = {inactive};");
        builder.AppendLine("    model.eval();");
        builder.AppendLine("}");
        builder.AppendLine();
    }

    private static string ResolvePath(string root, string path) => Path.IsPathRooted(path) ? path : Path.GetFullPath(path, root);

    private static string SanitizeIdentifier(string value)
    {
        StringBuilder builder = new(value.Length);
        foreach (char c in value)
        {
            builder.Append(char.IsLetterOrDigit(c) || c == '_' ? c : '_');
        }

        return builder.ToString();
    }
}
