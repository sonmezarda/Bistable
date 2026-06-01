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
        CancellationToken cancellationToken = default,
        IProgress<SimulationWorkerBuildProgress>? progress = null,
        Bistable.Core.Design.Ast.DesignAst? designAst = null)
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

            // On Windows, running executables cannot be overwritten. Delete the old binary
            // before building so the linker can create a fresh file.
            string executableNameEarly = OperatingSystem.IsWindows() ? "bistable-worker.exe" : "bistable-worker";
            string oldExecutablePath = Path.Combine(buildDirectory, executableNameEarly);
            if (OperatingSystem.IsWindows() && File.Exists(oldExecutablePath))
            {
                try { File.Delete(oldExecutablePath); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }

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
            progress?.Report(new SimulationWorkerBuildProgress("generate", "Generating C++ simulation wrapper..."));
            // Phase 3: when AST and probes are both available, emit a probe table
            // that maps hierarchical signal paths to model field accessors. When AST
            // is null (legacy callers) or probes are disabled, the probe-related
            // commands return ErrorResponse("probes disabled").
            IReadOnlyList<ProbeEntry> probes = (designAst is not null && configuration.EnableInternalProbes)
                ? ProbeTableEnumerator.Enumerate(designAst, configuration.TopModule).ToList()
                : [];
            await File.WriteAllTextAsync(
                wrapperPath,
                GenerateWorkerSource(configuration.TopModule, metadata, options, probes),
                cancellationToken);

            List<string> arguments = BuildVerilatorArguments(configuration, buildDirectory, projectDirectory, wrapperPath);

            progress?.Report(new SimulationWorkerBuildProgress("verilator", "Running Verilator C++ generation/build..."));
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(5));
            try
            {
                await _verilator.RunAsync(
                    arguments,
                    projectDirectory,
                    timeout.Token,
                    line => progress?.Report(new SimulationWorkerBuildProgress("verilator", line)));
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException("Native worker build exceeded the 5 minute timeout. Check the project sources and Verilator options.");
            }

            string executableName = OperatingSystem.IsWindows() ? "bistable-worker.exe" : "bistable-worker";
            string executablePath = Path.Combine(buildDirectory, executableName);
            if (!File.Exists(executablePath))
            {
                throw new InvalidOperationException($"Worker build completed but executable was not found: {executablePath}");
            }

            progress?.Report(new SimulationWorkerBuildProgress("ready", "Native worker executable built."));
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

    /// <summary>
    /// Assembles the Verilator command-line argument list for one worker build.
    /// Extracted from BuildAsync to keep that method's cognitive complexity
    /// under the 15-statement budget. Pure: no I/O, no async, no side effects.
    /// </summary>
    private static List<string> BuildVerilatorArguments(
        ProjectConfiguration configuration,
        string buildDirectory,
        string projectDirectory,
        string wrapperPath)
    {
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
            arguments.Add("-I" + ResolvePath(projectDirectory, includeDir));

        foreach (KeyValuePair<string, string> define in configuration.Defines)
            arguments.Add($"+define+{define.Key}={define.Value}");

        foreach (KeyValuePair<string, string> parameter in configuration.Parameters)
            arguments.Add("-G" + parameter.Key + "=" + parameter.Value);

        if (configuration.Trace.Enabled)
            arguments.Add("--trace");

        // Phase 3 (P3-2): expose every hierarchical signal as a public field
        // on the compiled Verilator model so the worker's probe table can read
        // and write internal signals at runtime. Conditional so that designs
        // sensitive to compile time / binary size can opt out via
        // ProjectConfiguration.EnableInternalProbes = false.
        if (configuration.EnableInternalProbes)
            arguments.Add("--public-flat-rw");

        arguments.AddRange(configuration.VerilatorOptions);
        arguments.AddRange(configuration.Sources.Select(source => ResolvePath(projectDirectory, source)));
        arguments.Add(wrapperPath);
        return arguments;
    }

    private static string GenerateWorkerSource(
        string topModule,
        ModuleMetadata metadata,
        WorkerGenerationOptions options,
        IReadOnlyList<ProbeEntry> probes)
    {
        string modelType = "V" + SanitizeIdentifier(topModule);
        StringBuilder builder = new();
        builder.AppendLine("#include <cstdint>");
        builder.AppendLine("#include <cstdlib>");
        builder.AppendLine("#include <cstdio>");
        builder.AppendLine("#include <functional>");
        builder.AppendLine("#include <iostream>");
        builder.AppendLine("#include <map>");
        builder.AppendLine("#include <memory>");
        builder.AppendLine("#include <regex>");
        builder.AppendLine("#include <sstream>");
        builder.AppendLine("#include <string>");
        builder.AppendLine("#include <tuple>");
        builder.AppendLine("#include <unordered_map>");
        builder.AppendLine("#include <vector>");
        // Redirect Verilator runtime diagnostics (%Error, %Warning) to stderr so they
        // never appear on stdout and corrupt the JSON protocol stream.
        builder.AppendLine("#define VL_PRINTF(...) fprintf(stderr, __VA_ARGS__)");
        builder.AppendLine("#include \"verilated.h\"");
        if (options.TraceEnabled)
        {
            builder.AppendLine("#include \"verilated_vcd_c.h\"");
        }
        builder.AppendLine($"#include \"{modelType}.h\"");
        // Phase 3: when probes are present we need the full ___024root definition
        // so the probe lambdas can dereference `model->rootp->{field}`. The main
        // header only forward-declares the root class.
        if (probes.Count > 0)
        {
            builder.AppendLine($"#include \"{modelType}___024root.h\"");
        }
        // Verilator 5.x requires sc_time_stamp() when not linking against SystemC.
        builder.AppendLine("double sc_time_stamp() { return 0; }");
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

        // Probe table support comes first so its apply_forced_signals() is in
        // scope for the subsequent drive_clock + apply_reset emitters (which
        // re-apply forced values after each eval).
        AppendProbeTableSupport(builder, modelType, probes);
        AppendDriveClockFunction(builder, modelType, metadata, options.TraceEnabled);
        AppendApplyResetFunction(builder, modelType, metadata, options, options.TraceEnabled);
        builder.AppendLine();
        // Emit a SimulationFrame: { "kind":"frame", "time":N, "signals":[...], "trace":[...] }.
        // The "kind" discriminator selects the SimulationFrame subtype of WorkerResponse
        // on the C# deserializer side (System.Text.Json polymorphic dispatch).
        builder.AppendLine("void write_snapshot(const " + modelType + "& model, std::uint64_t time, const trace_buffer& trace) {");
        builder.AppendLine("    std::cout << \"{\\\"kind\\\":\\\"frame\\\",\\\"time\\\":\" << time << \",\\\"signals\\\":[\";");
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
        // Phase 3: populate the probe table from the AST-derived signal list.
        // Empty when EnableInternalProbes=false or no AST was supplied at build time.
        builder.AppendLine("    init_probe_table(model.get());");
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
        builder.AppendLine("                apply_forced_signals();");
        builder.AppendLine("                model->eval();");
        if (options.TraceEnabled)
        {
            builder.AppendLine("                dump_trace(tracer.get(), trace_time);");
        }
        builder.AppendLine("                append_output_trace(*model, time, trace);");
        builder.AppendLine("                write_snapshot(*model, time, trace);");
        builder.AppendLine("            } else if (type == \"eval\" || type == \"getSnapshot\") {");
        builder.AppendLine("                apply_forced_signals();");
        builder.AppendLine("                model->eval();");
        if (options.TraceEnabled)
        {
            builder.AppendLine("                dump_trace(tracer.get(), trace_time);");
        }
        builder.AppendLine("                append_output_trace(*model, time, trace);");
        builder.AppendLine("                write_snapshot(*model, time, trace);");
        builder.AppendLine("            } else if (type == \"tick\") {");
        builder.AppendLine("                const std::string clock = get_string(line, \"signal\");");
        builder.AppendLine("                apply_forced_signals();");
        builder.AppendLine(options.TraceEnabled
            ? "                drive_clock(*model, tracer.get(), trace_time, clock, time, trace);"
            : "                drive_clock(*model, clock, time, trace);");
        builder.AppendLine("                ++time;");
        builder.AppendLine("                write_snapshot(*model, time, trace);");
        builder.AppendLine("            } else if (type == \"runCycles\") {");
        builder.AppendLine("                const std::uint64_t cycles = get_u64(line, \"cycles\", 1);");
        builder.AppendLine("                const std::string clock = get_string(line, \"signal\");");
        builder.AppendLine("                for (std::uint64_t i = 0; i < cycles; ++i) {");
        builder.AppendLine("                    apply_forced_signals();");
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
        // Phase 3: probe lambdas captured the OLD rootp pointer; rebuild the
        // probe table against the freshly-constructed model. Forced signals
        // persist across reset by design (the GUI's "this pin is held" semantics).
        builder.AppendLine("                init_probe_table(model.get());");
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
        // ── Phase 3 probe commands ───────────────────────────────────
        // The path-extraction line is identical across read/write/force/release;
        // factor into one const so the literal isn't duplicated (S1192).
        const string ExtractPath = "                const std::string path = get_string(line, \"path\");";
        builder.AppendLine("            } else if (type == \"readSignal\") {");
        builder.AppendLine(ExtractPath);
        builder.AppendLine("                auto it = probe_table.find(path);");
        builder.AppendLine("                if (it == probe_table.end()) { write_error(\"unknown probe path: \" + path); }");
        builder.AppendLine("                else { write_signal_read(path, it->second.read(), it->second.width, it->second.is_signed); }");
        builder.AppendLine("            } else if (type == \"writeSignal\") {");
        builder.AppendLine(ExtractPath);
        builder.AppendLine("                const std::string raw_value = get_string(line, \"value\");");
        builder.AppendLine("                auto it = probe_table.find(path);");
        builder.AppendLine("                if (it == probe_table.end()) { write_error(\"unknown probe path: \" + path); }");
        builder.AppendLine("                else { it->second.write(parse_u64(raw_value)); write_ack(); }");
        builder.AppendLine("            } else if (type == \"forceSignal\") {");
        builder.AppendLine(ExtractPath);
        builder.AppendLine("                const std::string raw_value = get_string(line, \"value\");");
        builder.AppendLine("                auto it = probe_table.find(path);");
        builder.AppendLine("                if (it == probe_table.end()) { write_error(\"unknown probe path: \" + path); }");
        builder.AppendLine("                else {");
        builder.AppendLine("                    const std::uint64_t v = parse_u64(raw_value);");
        builder.AppendLine("                    forced_signals[path] = v;");
        builder.AppendLine("                    it->second.write(v);   // apply immediately too");
        builder.AppendLine("                    write_ack();");
        builder.AppendLine("                }");
        builder.AppendLine("            } else if (type == \"releaseSignal\") {");
        builder.AppendLine(ExtractPath);
        builder.AppendLine("                forced_signals.erase(path);");
        builder.AppendLine("                write_ack();");
            // P3-6: memory probe handlers. Address + count are integer JSON fields.
        builder.AppendLine("            } else if (type == \"readMemory\") {");
        builder.AppendLine(ExtractPath);
        builder.AppendLine("                const std::uint64_t addr = get_u64(line, \"memoryAddress\", 0);");
        builder.AppendLine("                const std::uint64_t cnt = get_u64(line, \"memoryCount\", 1);");
        builder.AppendLine("                auto mit = memory_table.find(path);");
        builder.AppendLine("                if (mit == memory_table.end()) { write_error(\"unknown memory probe: \" + path); }");
        builder.AppendLine("                else { write_memory_read(path, addr, cnt, mit->second); }");
        builder.AppendLine("            } else if (type == \"writeMemory\") {");
        builder.AppendLine(ExtractPath);
        builder.AppendLine("                const std::uint64_t addr = get_u64(line, \"memoryAddress\", 0);");
        builder.AppendLine("                const std::string raw_value = get_string(line, \"value\");");
        builder.AppendLine("                auto mit = memory_table.find(path);");
        builder.AppendLine("                if (mit == memory_table.end()) { write_error(\"unknown memory probe: \" + path); }");
        builder.AppendLine("                else if (addr >= (std::uint64_t)mit->second.depth) { write_error(\"memory address out of range\"); }");
        builder.AppendLine("                else { mit->second.write((std::size_t)addr, parse_u64(raw_value)); write_ack(); }");
        builder.AppendLine("            } else if (type == \"listProbes\") {");
        builder.AppendLine("                write_probe_list();");
        builder.AppendLine("            } else {");
        builder.AppendLine("                write_error(\"unknown command type: \" + type);");
        builder.AppendLine("            }");
        builder.AppendLine("        } catch (const std::exception& ex) {");
        // Emit an ErrorResponse: { "kind":"error", "message":"..." }
        builder.AppendLine("            std::cout << \"{\\\"kind\\\":\\\"error\\\",\\\"message\\\":\\\"\" << json_escape(ex.what()) << \"\\\"}\" << std::endl;");
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

    /// <summary>
    /// Emits the C++ probe-table machinery (Phase 3): the probe map, the
    /// force-apply function, JSON encoders for the response types, and the
    /// init_probe_table function that maps every hierarchical signal path to
    /// a read/write closure over the Verilator model.
    /// <para>When <paramref name="probes"/> is empty (designAst was null or
    /// EnableInternalProbes=false), only the empty scaffolding is emitted —
    /// probe commands then return ErrorResponse cleanly.</para>
    /// </summary>
    private static void AppendProbeTableSupport(StringBuilder builder, string modelType, IReadOnlyList<ProbeEntry> probes)
    {
        // Probe accessor: a read/write lambda pair plus metadata. Stored in an
        // unordered_map keyed by dotted hierarchy path. Force state is a small
        // ordered map for deterministic re-apply order.
        builder.AppendLine("struct ProbeEntry {");
        builder.AppendLine("    std::function<std::uint64_t()> read;");
        builder.AppendLine("    std::function<void(std::uint64_t)> write;");
        builder.AppendLine("    int width;");
        builder.AppendLine("    bool is_signed;");
        builder.AppendLine("    bool is_registered;");
        builder.AppendLine("    bool is_memory;");
        builder.AppendLine("    int memory_depth;");
        builder.AppendLine("};");
        builder.AppendLine("static std::unordered_map<std::string, ProbeEntry> probe_table;");
        builder.AppendLine("static std::map<std::string, std::uint64_t> forced_signals;");
        // P3-6: memory probes go through a separate accessor map indexed by
        // the same hierarchy path. Scalar reads of a memory path return 0
        // (use readMemory instead); the metadata in probe_table is enough for
        // listProbes to advertise them.
        builder.AppendLine("struct MemoryAccessor {");
        builder.AppendLine("    std::function<std::uint64_t(std::size_t)> read;");
        builder.AppendLine("    std::function<void(std::size_t, std::uint64_t)> write;");
        builder.AppendLine("    int cell_width;");
        builder.AppendLine("    int depth;");
        builder.AppendLine("};");
        builder.AppendLine("static std::unordered_map<std::string, MemoryAccessor> memory_table;");
        builder.AppendLine();

        // init_probe_table: populated once per worker startup from the design's
        // AST signal list. The lambdas capture the model pointer; Verilator's
        // --public-flat-rw flag exposes every hierarchical signal as a CData /
        // SData / IData / QData field under model->rootp.
        builder.AppendLine($"void init_probe_table({modelType}* m) {{");
        if (probes.Count > 0)
        {
            // Access model fields via rootp (Verilator 5.x convention with public-flat-rw).
            builder.AppendLine("    auto* r = m->rootp;");
            foreach (ProbeEntry p in probes)
            {
                string field = p.FieldName;
                string pathEscaped = p.Path;   // dotted paths are JSON-safe; no escape needed
                string isSignedC = p.IsSigned ? "true" : "false";
                string isRegC    = p.IsRegistered ? "true" : "false";
                if (p.IsMemory)
                {
                    // P3-6: memory probes are listed in the probe_table for
                    // metadata (ListProbes) but read/write through the array
                    // indexer. ReadSignal/WriteSignal still return an error on
                    // memory paths (they need the address); readMemory /
                    // writeMemory below are the right entry points.
                    int depth = p.MemoryDepth ?? 0;
                    builder.AppendLine($"    probe_table[\"{pathEscaped}\"] = ProbeEntry{{");
                    builder.AppendLine($"        []() -> std::uint64_t {{ return 0; }},");   // scalar read disabled
                    builder.AppendLine($"        [](std::uint64_t) {{ }},");                  // scalar write disabled
                    builder.AppendLine($"        {p.Width}, {isSignedC}, false, true, {depth}");
                    builder.AppendLine("    };");
                    builder.AppendLine($"    memory_table[\"{pathEscaped}\"] = MemoryAccessor{{");
                    builder.AppendLine($"        [r](std::size_t i) -> std::uint64_t {{ return (std::uint64_t)(r->{field}[i]); }},");
                    builder.AppendLine($"        [r](std::size_t i, std::uint64_t v) {{ r->{field}[i] = decltype(r->{field}[i])(v); }},");
                    builder.AppendLine($"        {p.Width}, {depth}");
                    builder.AppendLine("    };");
                }
                else
                {
                    builder.AppendLine($"    probe_table[\"{pathEscaped}\"] = ProbeEntry{{");
                    builder.AppendLine($"        [r]() -> std::uint64_t {{ return (std::uint64_t)(r->{field}); }},");
                    builder.AppendLine($"        [r](std::uint64_t v) {{ r->{field} = decltype(r->{field})(v); }},");
                    builder.AppendLine($"        {p.Width}, {isSignedC}, {isRegC}, false, 0");
                    builder.AppendLine("    };");
                }
            }
        }
        builder.AppendLine("}");
        builder.AppendLine();

        // apply_forced_signals: called at the top of every eval/tick/runCycles
        // so the user's forced values survive simulation propagation.
        builder.AppendLine("void apply_forced_signals() {");
        builder.AppendLine("    for (const auto& kv : forced_signals) {");
        builder.AppendLine("        auto it = probe_table.find(kv.first);");
        builder.AppendLine("        if (it != probe_table.end()) it->second.write(kv.second);");
        builder.AppendLine("    }");
        builder.AppendLine("}");
        builder.AppendLine();

        // JSON encoders for the probe response shapes. We escape user strings
        // (paths can contain bracket characters from generate blocks etc.).
        builder.AppendLine("void write_signal_read(const std::string& path, std::uint64_t value, int width, bool is_signed) {");
        builder.AppendLine("    std::ostringstream val; val << \"0x\" << std::hex << value;");
        builder.AppendLine("    std::cout << \"{\\\"kind\\\":\\\"signalRead\\\",\\\"result\\\":{\\\"path\\\":\\\"\" << json_escape(path)");
        builder.AppendLine("              << \"\\\",\\\"value\\\":\\\"\" << val.str() << \"\\\",\\\"width\\\":\" << width");
        builder.AppendLine("              << \",\\\"isSigned\\\":\" << (is_signed ? \"true\" : \"false\") << \"}}\" << std::endl;");
        builder.AppendLine("}");
        builder.AppendLine();
        builder.AppendLine("void write_ack() { std::cout << \"{\\\"kind\\\":\\\"ack\\\"}\" << std::endl; }");
        builder.AppendLine("void write_error(const std::string& msg) {");
        builder.AppendLine("    std::cout << \"{\\\"kind\\\":\\\"error\\\",\\\"message\\\":\\\"\" << json_escape(msg) << \"\\\"}\" << std::endl;");
        builder.AppendLine("}");
        // P3-6: emit a MemoryReadResult — `{"kind":"memoryRead","result":{path,startAddress,cellWidth,cells:[hex,...]}}`.
        // Declared AFTER write_error so the in-range guard can call it.
        builder.AppendLine("void write_memory_read(const std::string& path, std::uint64_t addr, std::uint64_t count, const MemoryAccessor& mem) {");
        builder.AppendLine("    std::uint64_t depth = (std::uint64_t)mem.depth;");
        builder.AppendLine("    if (addr >= depth) { write_error(\"memory address out of range\"); return; }");
        builder.AppendLine("    if (addr + count > depth) count = depth - addr;");
        builder.AppendLine("    std::cout << \"{\\\"kind\\\":\\\"memoryRead\\\",\\\"result\\\":{\\\"path\\\":\\\"\" << json_escape(path)");
        builder.AppendLine("              << \"\\\",\\\"startAddress\\\":\" << addr");
        builder.AppendLine("              << \",\\\"cellWidth\\\":\" << mem.cell_width");
        builder.AppendLine("              << \",\\\"cells\\\":[\";");
        builder.AppendLine("    for (std::uint64_t i = 0; i < count; ++i) {");
        builder.AppendLine("        if (i > 0) std::cout << \",\";");
        builder.AppendLine("        std::ostringstream val; val << \"0x\" << std::hex << mem.read((std::size_t)(addr + i));");
        builder.AppendLine("        std::cout << \"\\\"\" << val.str() << \"\\\"\";");
        builder.AppendLine("    }");
        builder.AppendLine("    std::cout << \"]}}\" << std::endl;");
        builder.AppendLine("}");
        builder.AppendLine();
        builder.AppendLine();
        builder.AppendLine("void write_probe_list() {");
        builder.AppendLine("    std::cout << \"{\\\"kind\\\":\\\"probeList\\\",\\\"probes\\\":[\";");
        builder.AppendLine("    bool first = true;");
        builder.AppendLine("    for (const auto& kv : probe_table) {");
        builder.AppendLine("        if (!first) std::cout << \",\"; first = false;");
        builder.AppendLine("        std::cout << \"{\\\"path\\\":\\\"\" << json_escape(kv.first)");
        builder.AppendLine("                  << \"\\\",\\\"width\\\":\" << kv.second.width");
        builder.AppendLine("                  << \",\\\"isSigned\\\":\" << (kv.second.is_signed ? \"true\" : \"false\")");
        builder.AppendLine("                  << \",\\\"isRegistered\\\":\" << (kv.second.is_registered ? \"true\" : \"false\")");
        builder.AppendLine("                  << \",\\\"isMemory\\\":\" << (kv.second.is_memory ? \"true\" : \"false\")");
        builder.AppendLine("                  << \",\\\"memoryDepth\\\":\" << kv.second.memory_depth << \"}\";");
        builder.AppendLine("    }");
        builder.AppendLine("    std::cout << \"]}\" << std::endl;");
        builder.AppendLine("}");
        builder.AppendLine();
    }

    private static void AppendDriveClockFunction(StringBuilder builder, string modelType, ModuleMetadata metadata, bool traceEnabled)
    {
        string traceParameter = traceEnabled ? "trace_file_t* trace_file, std::uint64_t& trace_time, " : string.Empty;
        // Phase 3 force semantics: after every eval inside the clock toggle we
        // re-apply forced signals so user-pinned values survive the FF latch
        // on the rising edge. Without this, force-then-tick would just become
        // "write-then-let-FF-overwrite".
        string reForce = "apply_forced_signals(); ";
        builder.AppendLine("void drive_clock(" + modelType + "& model, " + traceParameter + "const std::string& clock, std::uint64_t time, trace_buffer& trace) {");
        builder.AppendLine("    if (clock.empty()) { model.eval(); " + reForce + (traceEnabled ? "dump_trace(trace_file, trace_time); " : string.Empty) + "append_output_trace(model, time, trace); return; }");
        foreach (SignalPort port in metadata.Inputs.Where(static port => port.Width == 1))
        {
            builder.AppendLine($"    if (clock == \"{port.Name}\") {{");
            builder.AppendLine($"        model.{port.Name} = 0; append_trace(trace, \"{port.Name}\", \"0\", time); model.eval(); {reForce}{(traceEnabled ? "dump_trace(trace_file, trace_time); " : string.Empty)}append_output_trace(model, time, trace);");
            builder.AppendLine($"        model.{port.Name} = 1; append_trace(trace, \"{port.Name}\", \"1\", time); model.eval(); {reForce}{(traceEnabled ? "dump_trace(trace_file, trace_time); " : string.Empty)}append_output_trace(model, time, trace);");
            builder.AppendLine($"        model.{port.Name} = 0; append_trace(trace, \"{port.Name}\", \"0\", time + 1); model.eval(); {reForce}{(traceEnabled ? "dump_trace(trace_file, trace_time); " : string.Empty)}append_output_trace(model, time + 1, trace);");
            builder.AppendLine("        return;");
            builder.AppendLine("    }");
        }

        builder.AppendLine("    model.eval();");
        builder.AppendLine("    apply_forced_signals();");
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
