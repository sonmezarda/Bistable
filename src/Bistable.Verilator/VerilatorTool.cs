using System.Diagnostics;
using System.Text;
using Bistable.Core.Projects;

namespace Bistable.Verilator;

public sealed class VerilatorTool
{
    private readonly WindowsMsys2Locator.Msys2Paths? _msys2;

    public string ExecutablePath { get; }

    public VerilatorTool(string executablePath = "verilator")
    {
        if (OperatingSystem.IsWindows() && executablePath == "verilator")
        {
            _msys2 = WindowsMsys2Locator.Detect();
            ExecutablePath = _msys2?.VerilatorExecutable ?? executablePath;
        }
        else
        {
            ExecutablePath = executablePath;
        }
    }

    public async Task<string> GetVersionAsync(CancellationToken cancellationToken = default)
    {
        ProcessResult result = await RunProcessAsync(ExecutablePath, ["--version"], null, cancellationToken);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"Verilator failed: {result.StandardError}");
        }

        return result.StandardOutput.Trim();
    }

    public async Task GenerateXmlAsync(
        ProjectConfiguration configuration,
        string projectDirectory,
        string outputXmlPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputXmlPath);

        Directory.CreateDirectory(Path.GetDirectoryName(outputXmlPath) ?? projectDirectory);

        List<string> arguments =
        [
            "--xml-only",
            "-Wno-DEPRECATED",
            "--xml-output",
            outputXmlPath,
            "--top-module",
            configuration.TopModule
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

        ProcessResult result = await RunProcessAsync(ExecutablePath, arguments, projectDirectory, cancellationToken);
        if (result.ExitCode != 0)
        {
            // Verilator 5.040+ treats --xml-only as deprecated and exits with code 1 even
            // when the XML was successfully generated. If the output exists and the only
            // %Error line is the warning-summary "Exiting due to N warning(s)", treat it as success.
            bool onlyDeprecatedWarnings = result.ExitCode == 1
                && result.StandardError.Contains("%Warning-DEPRECATED", StringComparison.Ordinal)
                && result.StandardError
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .Where(static line => line.Contains("%Error:", StringComparison.Ordinal))
                    .All(static line => line.Contains("Exiting due to", StringComparison.Ordinal))
                && File.Exists(outputXmlPath);

            if (!onlyDeprecatedWarnings)
            {
                throw new InvalidOperationException(
                    $"Verilator XML generation failed with exit code {result.ExitCode}.{Environment.NewLine}{result.StandardError}");
            }
        }
    }

    public async Task RunAsync(
        IReadOnlyList<string> arguments,
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        ProcessResult result = _msys2 is not null
            ? await RunViaBashAsync(arguments, workingDirectory, cancellationToken)
            : await RunProcessAsync(ExecutablePath, arguments, workingDirectory, cancellationToken);

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Verilator failed with exit code {result.ExitCode}.{Environment.NewLine}{result.StandardError}");
        }
    }

    private async Task<ProcessResult> RunViaBashAsync(
        IReadOnlyList<string> arguments,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        string msys2Root = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(_msys2!.VerilatorExecutable)!, "..", ".."));
        string bashPath = Path.Combine(msys2Root, "usr", "bin", "bash.exe");

        string ucrt64Bin = ToCygpath(Path.Combine(msys2Root, "ucrt64", "bin"));
        string usrBin = ToCygpath(Path.Combine(msys2Root, "usr", "bin"));
        string verilatorRoot = ToCygpath(_msys2.VerilatorRoot);
        string verilatorExe = ToCygpath(_msys2.VerilatorExecutable);
        string pathEnv = $"PATH={ucrt64Bin}:{usrBin}:$PATH VERILATOR_ROOT={verilatorRoot}";

        // When --build is requested, Verilator generates a Makefile that embeds a Windows-style
        // VERILATOR_ROOT path. Running make with that path fails in MSYS2 because it is treated
        // as a relative path. Split into two steps: generate the Makefile, then invoke make with
        // an explicit VERILATOR_ROOT override so the correct unix path is used.
        bool hasBuild = arguments.Contains("--build", StringComparer.Ordinal);
        if (hasBuild)
        {
            List<string> generateArgs = arguments.Where(a => a != "--build").ToList();
            string generateCmd = BuildBashCommand(ToCygpath(workingDirectory), pathEnv, verilatorExe, generateArgs);
            ProcessResult generateResult = await RunProcessAsync(bashPath, ["-c", generateCmd], null, cancellationToken);
            if (generateResult.ExitCode != 0)
            {
                return generateResult;
            }

            string? mdir = FindArgumentValue(arguments, "--Mdir");
            string? topModule = FindArgumentValue(arguments, "--top-module");
            if (mdir is null || topModule is null)
            {
                return generateResult;
            }

            string makefile = $"V{SanitizeForMakefile(topModule)}.mk";
            string mdirCygpath = ToCygpath(mdir);
            string makeCmd = $"cd {ShellEscape(mdirCygpath)} && {pathEnv} make VERILATOR_ROOT={verilatorRoot} -f {ShellEscape(makefile)} -j 1";
            return await RunProcessAsync(bashPath, ["-c", makeCmd], null, cancellationToken);
        }

        string singleCmd = BuildBashCommand(ToCygpath(workingDirectory), pathEnv, verilatorExe, arguments);
        return await RunProcessAsync(bashPath, ["-c", singleCmd], null, cancellationToken);
    }

    private static string BuildBashCommand(string cwdCygpath, string pathEnv, string verilatorExe, IEnumerable<string> arguments)
    {
        IEnumerable<string> convertedArgs = arguments.Select(a => ShellEscape(ToCygpath(a)));
        return $"cd {ShellEscape(cwdCygpath)} && {pathEnv} {verilatorExe} {string.Join(" ", convertedArgs)}";
    }

    private static string? FindArgumentValue(IReadOnlyList<string> arguments, string flag)
    {
        for (int i = 0; i < arguments.Count - 1; i++)
        {
            if (string.Equals(arguments[i], flag, StringComparison.Ordinal))
            {
                return arguments[i + 1];
            }
        }
        return null;
    }

    private static string SanitizeForMakefile(string name)
    {
        StringBuilder sb = new(name.Length);
        foreach (char c in name)
        {
            sb.Append(char.IsLetterOrDigit(c) || c == '_' ? c : '_');
        }
        return sb.ToString();
    }

    private static string ToCygpath(string windowsPath)
    {
        if (string.IsNullOrEmpty(windowsPath))
        {
            return windowsPath;
        }

        string path = windowsPath.Replace('\\', '/');
        if (path.Length >= 2 && char.IsLetter(path[0]) && path[1] == ':')
        {
            path = "/" + char.ToLower(path[0]) + path[2..];
        }

        return path;
    }

    private static string ShellEscape(string value) => "'" + value.Replace("'", "'\\''") + "'";

    private static string ResolvePath(string root, string path) => Path.IsPathRooted(path) ? path : Path.GetFullPath(path, root);

    private async Task<ProcessResult> RunProcessAsync(
        string executable,
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        CancellationToken cancellationToken)
    {
        using Process process = new()
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = executable,
                WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            }
        };

        if (_msys2 is not null)
        {
            string currentPath = process.StartInfo.EnvironmentVariables["PATH"] ?? string.Empty;
            process.StartInfo.EnvironmentVariables["PATH"] = _msys2.ExtraPath + ";" + currentPath;
            process.StartInfo.EnvironmentVariables["VERILATOR_ROOT"] = _msys2.VerilatorRoot;
        }

        foreach (string argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        StringBuilder stdout = new();
        StringBuilder stderr = new();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                stdout.AppendLine(e.Data);
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                stderr.AppendLine(e.Data);
            }
        };

        if (!process.Start())
        {
            throw new InvalidOperationException($"Failed to start '{executable}'.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(cancellationToken);

        return new ProcessResult(process.ExitCode, stdout.ToString(), stderr.ToString());
    }

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
}
