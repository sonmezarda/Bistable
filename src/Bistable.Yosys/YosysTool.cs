using System.Diagnostics;
using System.Text;
using Bistable.Core.Projects;

namespace Bistable.Yosys;

/// <summary>
/// Phase 6 P6-3: wraps the external <c>yosys</c> binary. Mirrors the
/// <c>VerilatorTool</c> shape so the rest of the app gets a familiar surface
/// (locate executable → version probe → run with a script → output JSON).
///
/// The tool itself does NOT generate scripts — it consumes whatever script
/// the caller passes in (typically built by <see cref="YosysScriptBuilder"/>
/// from the project's <see cref="SynthesisConfiguration"/>). Keeping
/// orchestration separate from process invocation makes the tool trivial to
/// fake in tests.
/// </summary>
public sealed class YosysTool
{
    public string ExecutablePath { get; }

    public YosysTool(string executablePath = "yosys")
    {
        ExecutablePath = executablePath;
    }

    /// <summary>
    /// Returns true when <c>yosys</c> is reachable on PATH (or wherever
    /// <see cref="ExecutablePath"/> points). Used by the GUI to gate the
    /// Synthesize action — without yosys installed, the action stays disabled
    /// instead of throwing on click.
    /// </summary>
    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            ProcessResult result = await RunProcessAsync(ExecutablePath, ["-V"], null, cancellationToken);
            return result.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Win32Exception is what Process.Start raises on Linux when the
            // executable can't be found — we treat that as "not installed".
            return false;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
    }

    /// <summary>Returns the first line of <c>yosys -V</c>. Throws when yosys isn't reachable.</summary>
    public async Task<string> GetVersionAsync(CancellationToken cancellationToken = default)
    {
        ProcessResult result = await RunProcessAsync(ExecutablePath, ["-V"], null, cancellationToken);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"Yosys failed: {result.StandardError}");
        }
        // yosys prints e.g. "Yosys 0.33+96 (git sha1 …)" on stdout.
        return result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
    }

    /// <summary>
    /// Run a Yosys script. Captures the full stdout/stderr stream so the GUI
    /// can show synthesis logs in a diagnostic panel. Throws when yosys exits
    /// non-zero; the caller can inspect <see cref="YosysRunResult.StandardError"/>
    /// before the throw is swallowed if it catches.
    /// </summary>
    public async Task<YosysRunResult> RunScriptAsync(
        string scriptPath,
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scriptPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        if (!File.Exists(scriptPath))
        {
            throw new FileNotFoundException($"Yosys script not found: {scriptPath}", scriptPath);
        }
        ProcessResult result = await RunProcessAsync(
            ExecutablePath,
            ["-q", "-s", scriptPath],
            workingDirectory,
            cancellationToken);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Yosys synthesis failed with exit code {result.ExitCode}.{Environment.NewLine}{result.StandardError}");
        }
        return new YosysRunResult(result.StandardOutput, result.StandardError, result.ExitCode);
    }

    private static async Task<ProcessResult> RunProcessAsync(
        string executable,
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        CancellationToken cancellationToken,
        Action<string>? output = null)
    {
        ProcessStartInfo psi = new()
        {
            FileName = executable,
            WorkingDirectory = workingDirectory ?? string.Empty,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (string a in arguments) psi.ArgumentList.Add(a);

        using Process process = new() { StartInfo = psi, EnableRaisingEvents = true };
        StringBuilder stdout = new();
        StringBuilder stderr = new();
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                stdout.AppendLine(e.Data);
                output?.Invoke(e.Data);
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null) stderr.AppendLine(e.Data);
        };

        if (!process.Start())
        {
            throw new InvalidOperationException($"Failed to start process: {executable}");
        }
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(true); } catch { /* best effort */ }
            throw;
        }
        return new ProcessResult(stdout.ToString(), stderr.ToString(), process.ExitCode);
    }

    private sealed record ProcessResult(string StandardOutput, string StandardError, int ExitCode);
}

/// <summary>Public capture of a successful synthesis run.</summary>
public sealed record YosysRunResult(string StandardOutput, string StandardError, int ExitCode);
