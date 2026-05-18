using System.Diagnostics;
using System.Text;
using Bistable.Core.Projects;

namespace Bistable.Verilator;

public sealed class VerilatorTool(string executablePath = "verilator")
{
    public string ExecutablePath { get; } = executablePath;

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
            throw new InvalidOperationException(
                $"Verilator XML generation failed with exit code {result.ExitCode}.{Environment.NewLine}{result.StandardError}");
        }
    }

    public async Task RunAsync(
        IReadOnlyList<string> arguments,
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        ProcessResult result = await RunProcessAsync(ExecutablePath, arguments, workingDirectory, cancellationToken);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Verilator failed with exit code {result.ExitCode}.{Environment.NewLine}{result.StandardError}");
        }
    }

    private static string ResolvePath(string root, string path) => Path.IsPathRooted(path) ? path : Path.GetFullPath(path, root);

    private static async Task<ProcessResult> RunProcessAsync(
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
