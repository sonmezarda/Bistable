using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Collections.Concurrent;
using Bistable.Core.Design;
using Bistable.Core.Projects;

namespace Bistable.Verilator;

public sealed class SimulationWorkerBuilder(string verilatorExecutablePath = "verilator")
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> BuildLocks = new(StringComparer.Ordinal);
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
        SemaphoreSlim buildLock = BuildLocks.GetOrAdd(buildDirectory, static _ => new SemaphoreSlim(1, 1));
        await buildLock.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(buildDirectory);

            string wrapperPath = Path.Combine(buildDirectory, "bistable_worker.cpp");
            string? traceFilePath = configuration.Trace.Enabled
                ? Path.Combine(buildDirectory, "bistable-trace.vcd")
                : null;
            WorkerGenerationOptions options = new(
                configuration.Clocks.FirstOrDefault()?.Name,
                configuration.Resets.FirstOrDefault()?.Name,
                configuration.Resets.FirstOrDefault()?.ActiveLevel ?? 0,
                configuration.Trace.Enabled,
                configuration.Trace.Depth,
                traceFilePath is null ? string.Empty : Path.GetFileName(traceFilePath));
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

            if (configuration.Trace.Enabled)
            {
                arguments.Add("--trace");
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

            EnsureExecutablePermissions(executablePath);
            return new SimulationWorkerBuildResult(executablePath, buildDirectory, traceFilePath);
        }
        finally
        {
            buildLock.Release();
        }
    }

    private static void EnsureExecutablePermissions(string executablePath)
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            return;
        }

        File.SetUnixFileMode(
            executablePath,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
            | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
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
        builder.AppendLine("#include <tuple>");
        builder.AppendLine("#include <vector>");
        builder.AppendLine("#include \"verilated.h\"");
        if (options.TraceEnabled)
        {
            builder.AppendLine("#include \"verilated_vcd_c.h\"");
        }
        builder.AppendLine($"#include \"{modelType}.h\"");
        builder.AppendLine();
        builder.AppendLine("namespace {");
        builder.AppendLine("using trace_entry = std::tuple<std::string, std::string, std::uint64_t>;");
        builder.AppendLine("using trace_buffer = std::vector<trace_entry>;");
        if (options.TraceEnabled)
        {
            builder.AppendLine("using trace_file_t = VerilatedVcdC;");
        }
        builder.AppendLine();
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
        builder.AppendLine("std::string to_decimal_string(std::uint64_t value) {");
        builder.AppendLine("    return std::to_string(static_cast<unsigned long long>(value));");
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
        builder.AppendLine("void append_trace(trace_buffer& trace, const std::string& signal, const std::string& value, std::uint64_t time) {");
        builder.AppendLine("    trace.emplace_back(signal, value, time);");
        builder.AppendLine("}");
        builder.AppendLine();
        builder.AppendLine("void append_output_trace(const " + modelType + "& model, std::uint64_t time, trace_buffer& trace) {");
        foreach (SignalPort port in metadata.Outputs)
        {
            builder.AppendLine($"    append_trace(trace, \"{port.Name}\", to_decimal_string(static_cast<std::uint64_t>(model.{port.Name})), time);");
        }
        builder.AppendLine("}");
        builder.AppendLine();
        builder.AppendLine("void write_signals(const " + modelType + "& model) {");
        bool first = true;
        foreach (SignalPort port in metadata.Outputs)
        {
            string separator = first ? string.Empty : ",";
            first = false;
            builder.AppendLine($"    std::cout << \"{separator}{{\\\"signal\\\":\\\"{port.Name}\\\",\\\"value\\\":\\\"\" << static_cast<unsigned long long>(model.{port.Name}) << \"\\\",\\\"time\\\":0}}\";");
        }
        builder.AppendLine("}");
        builder.AppendLine();
        builder.AppendLine("void write_trace(const trace_buffer& trace) {");
        builder.AppendLine("    for (std::size_t index = 0; index < trace.size(); ++index) {");
        builder.AppendLine("        if (index > 0) {");
        builder.AppendLine("            std::cout << \",\";");
        builder.AppendLine("        }");
        builder.AppendLine("        const auto& entry = trace[index];");
        builder.AppendLine("        std::cout << \"{\\\"signal\\\":\\\"\" << json_escape(std::get<0>(entry)) << \"\\\",\\\"value\\\":\\\"\" << json_escape(std::get<1>(entry)) << \"\\\",\\\"time\\\":\" << std::get<2>(entry) << \"}\";");
        builder.AppendLine("    }");
        builder.AppendLine("}");
        builder.AppendLine();
        if (options.TraceEnabled)
        {
            AppendTraceSupport(builder, modelType, options);
        }

        AppendDriveClockFunction(builder, modelType, metadata, options.TraceEnabled);
        AppendApplyResetFunction(builder, modelType, metadata, options, options.TraceEnabled);
        builder.AppendLine();
        builder.AppendLine("void write_snapshot(const " + modelType + "& model, std::uint64_t time, const trace_buffer& trace) {");
        builder.AppendLine("    std::cout << \"{\\\"time\\\":\" << time << \",\\\"signals\\\":[\";");
        builder.AppendLine("    write_signals(model);");
        builder.AppendLine("    std::cout << \"],\\\"trace\\\":[\";");
        builder.AppendLine("    write_trace(trace);");
        builder.AppendLine("    std::cout << \"]}\" << std::endl;");
        builder.AppendLine("}");
        builder.AppendLine("}");
        builder.AppendLine();
        builder.AppendLine("int main(int argc, char** argv) {");
        builder.AppendLine("    Verilated::commandArgs(argc, argv);");
        builder.AppendLine($"    auto model = std::make_unique<{modelType}>();");
        if (options.TraceEnabled)
        {
            builder.AppendLine("    auto tracer = create_trace(*model);");
        }
        builder.AppendLine("    std::uint64_t time = 0;");
        if (options.TraceEnabled)
        {
            builder.AppendLine("    std::uint64_t trace_time = 0;");
        }
        builder.AppendLine("    std::string line;");
        builder.AppendLine("    while (std::getline(std::cin, line)) {");
        builder.AppendLine("        try {");
        builder.AppendLine("            const std::string type = get_string(line, \"type\");");
        builder.AppendLine("            trace_buffer trace;");
        builder.AppendLine("            if (type == \"setInput\") {");
        builder.AppendLine("                const std::string signal = get_string(line, \"signal\");");
        builder.AppendLine("                const std::string raw_value = get_string(line, \"value\");");
        builder.AppendLine("                const std::uint64_t value = parse_u64(raw_value);");
        foreach (SignalPort port in metadata.Inputs)
        {
            builder.AppendLine($"                if (signal == \"{port.Name}\") model->{port.Name} = value;");
        }

        builder.AppendLine("                append_trace(trace, signal, raw_value, time);");
        builder.AppendLine("                model->eval();");
        if (options.TraceEnabled)
        {
            builder.AppendLine("                dump_trace(tracer.get(), trace_time);");
        }
        builder.AppendLine("                append_output_trace(*model, time, trace);");
        builder.AppendLine("                write_snapshot(*model, time, trace);");
        builder.AppendLine("            } else if (type == \"eval\" || type == \"getSnapshot\") {");
        builder.AppendLine("                model->eval();");
        if (options.TraceEnabled)
        {
            builder.AppendLine("                dump_trace(tracer.get(), trace_time);");
        }
        builder.AppendLine("                append_output_trace(*model, time, trace);");
        builder.AppendLine("                write_snapshot(*model, time, trace);");
        builder.AppendLine("            } else if (type == \"tick\") {");
        builder.AppendLine("                const std::string clock = get_string(line, \"signal\");");
        builder.AppendLine(options.TraceEnabled
            ? "                drive_clock(*model, tracer.get(), trace_time, clock, time, trace);"
            : "                drive_clock(*model, clock, time, trace);");
        builder.AppendLine("                ++time;");
        builder.AppendLine("                write_snapshot(*model, time, trace);");
        builder.AppendLine("            } else if (type == \"runCycles\") {");
        builder.AppendLine("                const std::uint64_t cycles = get_u64(line, \"cycles\", 1);");
        builder.AppendLine("                const std::string clock = get_string(line, \"signal\");");
        builder.AppendLine("                for (std::uint64_t i = 0; i < cycles; ++i) {");
        builder.AppendLine("                    if (!clock.empty()) {");
        builder.AppendLine(options.TraceEnabled
            ? "                        drive_clock(*model, tracer.get(), trace_time, clock, time, trace);"
            : "                        drive_clock(*model, clock, time, trace);");
        builder.AppendLine("                    }");
        builder.AppendLine("                    ++time;");
        builder.AppendLine("                }");
        builder.AppendLine("                write_snapshot(*model, time, trace);");
        builder.AppendLine("            } else if (type == \"reset\") {");
        builder.AppendLine("                time = 0;");
        if (options.TraceEnabled)
        {
            builder.AppendLine("                if (tracer) { tracer->close(); }");
        }
        builder.AppendLine("                model = std::make_unique<" + modelType + ">();");
        if (options.TraceEnabled)
        {
            builder.AppendLine("                tracer = create_trace(*model);");
            builder.AppendLine("                trace_time = 0;");
            builder.AppendLine("                apply_reset(*model, tracer.get(), trace_time, time, trace);");
        }
        else
        {
            builder.AppendLine("                apply_reset(*model, time, trace);");
        }
        builder.AppendLine("                write_snapshot(*model, time, trace);");
        builder.AppendLine("            } else if (type == \"pause\") {");
        builder.AppendLine("                write_snapshot(*model, time, trace);");
        builder.AppendLine("            }");
        builder.AppendLine("        } catch (const std::exception& ex) {");
        builder.AppendLine("            std::cout << \"{\\\"time\\\":\" << time << \",\\\"signals\\\":[],\\\"trace\\\":[],\\\"error\\\":\\\"\" << json_escape(ex.what()) << \"\\\"}\" << std::endl;");
        builder.AppendLine("        }");
        builder.AppendLine("    }");
        if (options.TraceEnabled)
        {
            builder.AppendLine("    if (tracer) { tracer->close(); }");
        }
        builder.AppendLine("    return 0;");
        builder.AppendLine("}");

        return builder.ToString();
    }

    private static void AppendTraceSupport(StringBuilder builder, string modelType, WorkerGenerationOptions options)
    {
        builder.AppendLine("std::unique_ptr<trace_file_t> create_trace(" + modelType + "& model) {");
        builder.AppendLine("    Verilated::traceEverOn(true);");
        builder.AppendLine("    auto trace = std::make_unique<trace_file_t>();");
        builder.AppendLine("    model.trace(trace.get(), " + Math.Max(1, options.TraceDepth).ToString(CultureInfo.InvariantCulture) + ");");
        builder.AppendLine("    trace->open(\"" + options.TraceFileName + "\");");
        builder.AppendLine("    return trace;");
        builder.AppendLine("}");
        builder.AppendLine();
        builder.AppendLine("void dump_trace(trace_file_t* trace, std::uint64_t& trace_time) {");
        builder.AppendLine("    if (trace == nullptr) {");
        builder.AppendLine("        return;");
        builder.AppendLine("    }");
        builder.AppendLine("    trace->dump(static_cast<vluint64_t>(trace_time));");
        builder.AppendLine("    trace->flush();");
        builder.AppendLine("    ++trace_time;");
        builder.AppendLine("}");
        builder.AppendLine();
    }

    private static void AppendDriveClockFunction(StringBuilder builder, string modelType, ModuleMetadata metadata, bool traceEnabled)
    {
        string traceParameter = traceEnabled ? "trace_file_t* trace_file, std::uint64_t& trace_time, " : string.Empty;
        builder.AppendLine("void drive_clock(" + modelType + "& model, " + traceParameter + "const std::string& clock, std::uint64_t time, trace_buffer& trace) {");
        builder.AppendLine("    if (clock.empty()) { model.eval();" + (traceEnabled ? " dump_trace(trace_file, trace_time);" : string.Empty) + " append_output_trace(model, time, trace); return; }");
        foreach (SignalPort port in metadata.Inputs.Where(static port => port.Width == 1))
        {
            builder.AppendLine($"    if (clock == \"{port.Name}\") {{");
            builder.AppendLine($"        model.{port.Name} = 0; append_trace(trace, \"{port.Name}\", \"0\", time); model.eval();{(traceEnabled ? " dump_trace(trace_file, trace_time);" : string.Empty)} append_output_trace(model, time, trace);");
            builder.AppendLine($"        model.{port.Name} = 1; append_trace(trace, \"{port.Name}\", \"1\", time); model.eval();{(traceEnabled ? " dump_trace(trace_file, trace_time);" : string.Empty)} append_output_trace(model, time, trace);");
            builder.AppendLine($"        model.{port.Name} = 0; append_trace(trace, \"{port.Name}\", \"0\", time + 1); model.eval();{(traceEnabled ? " dump_trace(trace_file, trace_time);" : string.Empty)} append_output_trace(model, time + 1, trace);");
            builder.AppendLine("        return;");
            builder.AppendLine("    }");
        }

        builder.AppendLine("    model.eval();");
        if (traceEnabled)
        {
            builder.AppendLine("    dump_trace(trace_file, trace_time);");
        }
        builder.AppendLine("    append_output_trace(model, time, trace);");
        builder.AppendLine("}");
        builder.AppendLine();
    }

    private static void AppendApplyResetFunction(
        StringBuilder builder,
        string modelType,
        ModuleMetadata metadata,
        WorkerGenerationOptions options,
        bool traceEnabled)
    {
        string? reset = options.ResetSignal is not null && metadata.Inputs.Any(port => port.Name == options.ResetSignal && port.Width == 1)
            ? options.ResetSignal
            : null;
        string? clock = options.DefaultClock is not null && metadata.Inputs.Any(port => port.Name == options.DefaultClock && port.Width == 1)
            ? options.DefaultClock
            : null;

        string traceParameter = traceEnabled ? "trace_file_t* trace_file, std::uint64_t& trace_time, " : string.Empty;
        builder.AppendLine("void apply_reset(" + modelType + "& model, " + traceParameter + "std::uint64_t time, trace_buffer& trace) {");
        if (reset is null)
        {
            builder.AppendLine("    model.eval();");
            if (traceEnabled)
            {
                builder.AppendLine("    dump_trace(trace_file, trace_time);");
            }
            builder.AppendLine("    append_output_trace(model, time, trace);");
            builder.AppendLine("}");
            builder.AppendLine();
            return;
        }

        int active = options.ResetActiveLevel == 0 ? 0 : 1;
        int inactive = active == 0 ? 1 : 0;
        builder.AppendLine($"    model.{reset} = {active};");
        builder.AppendLine($"    append_trace(trace, \"{reset}\", \"{active}\", time);");
        builder.AppendLine("    model.eval();");
        if (traceEnabled)
        {
            builder.AppendLine("    dump_trace(trace_file, trace_time);");
        }
        builder.AppendLine("    append_output_trace(model, time, trace);");
        if (clock is not null)
        {
            builder.AppendLine(traceEnabled
                ? $"    drive_clock(model, trace_file, trace_time, \"{clock}\", time, trace);"
                : $"    drive_clock(model, \"{clock}\", time, trace);");
        }

        builder.AppendLine($"    model.{reset} = {inactive};");
        builder.AppendLine($"    append_trace(trace, \"{reset}\", \"{inactive}\", time);");
        builder.AppendLine("    model.eval();");
        if (traceEnabled)
        {
            builder.AppendLine("    dump_trace(trace_file, trace_time);");
        }
        builder.AppendLine("    append_output_trace(model, time, trace);");
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
