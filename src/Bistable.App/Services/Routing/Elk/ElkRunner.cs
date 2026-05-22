using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Bistable.App.Services.Routing.Elk;

/// <summary>
/// Runs the elk-router Node script: serialises an <see cref="ElkGraph"/> to JSON,
/// pipes it through stdin, and deserialises the layouted graph from stdout.
/// </summary>
public sealed class ElkRunner
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(8);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    // Node's JSON.parse rejects the UTF-8 BOM that Encoding.UTF8 emits on stream writes.
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private readonly string _nodeExecutable;
    private readonly string _scriptPath;
    private readonly TimeSpan _timeout;

    public ElkRunner()
        : this("node", ResolveDefaultScriptPath(), DefaultTimeout)
    {
    }

    public ElkRunner(string nodeExecutable, string scriptPath, TimeSpan timeout)
    {
        _nodeExecutable = nodeExecutable;
        _scriptPath = scriptPath;
        _timeout = timeout;
    }

    public ElkGraph Layout(ElkGraph input)
    {
        string requestJson;
        try
        {
            requestJson = JsonSerializer.Serialize(input, JsonOptions);
        }
        catch (Exception ex)
        {
            throw new SchematicRoutingException($"Failed to serialize ELK request: {ex.Message}", ex);
        }

        string responseJson = Invoke(requestJson);
        if (string.IsNullOrWhiteSpace(responseJson))
        {
            throw new SchematicRoutingException("ELK returned an empty response.");
        }

        try
        {
            ElkGraph? result = JsonSerializer.Deserialize<ElkGraph>(responseJson, JsonOptions);
            if (result is null)
            {
                throw new SchematicRoutingException("ELK returned a null layout.");
            }

            return result;
        }
        catch (JsonException ex)
        {
            string snippet = responseJson.Length > 200 ? responseJson[..200] + "…" : responseJson;
            throw new SchematicRoutingException(
                $"ELK returned invalid JSON ({ex.Message}). Response head: {snippet}",
                ex);
        }
    }

    /// <summary>Returns the dumped ELK request JSON. Useful for diagnostics.</summary>
    public static string SerializeForDebug(ElkGraph input) => JsonSerializer.Serialize(input, JsonOptions);

    private string Invoke(string requestJson)
    {
        if (!File.Exists(_scriptPath))
        {
            throw new SchematicRoutingException($"ELK router script not found at '{_scriptPath}'. Run 'npm install' inside tools/elk-router/.");
        }

        ProcessStartInfo startInfo = new()
        {
            FileName = _nodeExecutable,
            Arguments = $"\"{_scriptPath}\"",
            WorkingDirectory = Path.GetDirectoryName(_scriptPath) ?? Environment.CurrentDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardInputEncoding = Utf8NoBom,
            StandardOutputEncoding = Utf8NoBom,
            StandardErrorEncoding = Utf8NoBom
        };

        try
        {
            using Process process = Process.Start(startInfo)
                ?? throw new SchematicRoutingException("Failed to start the ELK router process.");

            process.StandardInput.Write(requestJson);
            process.StandardInput.Close();

            Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
            Task<string> stderrTask = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit((int)_timeout.TotalMilliseconds))
            {
                TryKillProcess(process);
                throw new SchematicRoutingException($"ELK routing timed out after {_timeout.TotalSeconds:0.#} seconds.");
            }

            string stdout = stdoutTask.GetAwaiter().GetResult();
            string stderr = stderrTask.GetAwaiter().GetResult();
            if (process.ExitCode != 0)
            {
                throw new SchematicRoutingException(
                    string.IsNullOrWhiteSpace(stderr)
                        ? $"ELK router exited with code {process.ExitCode}."
                        : $"ELK router exited with code {process.ExitCode}: {stderr.Trim()}");
            }

            return stdout;
        }
        catch (Win32Exception ex)
        {
            throw new SchematicRoutingException(
                $"Node.js executable '{_nodeExecutable}' not found. Install Node.js (>= 18) to enable the ELK router.",
                ex);
        }
    }

    private static void TryKillProcess(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // Process already exited.
        }
    }

    private static string ResolveDefaultScriptPath()
    {
        // Walk up from the executable directory looking for tools/elk-router/elk-router.js.
        // Covers both `dotnet run` (bin/Release/net10.0) and packaged layouts that
        // sit alongside the tools/ directory.
        foreach (string searchRoot in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
        {
            string? dir = searchRoot;
            while (!string.IsNullOrEmpty(dir))
            {
                string candidate = Path.Combine(dir, "tools", "elk-router", "elk-router.js");
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                DirectoryInfo? parent = Directory.GetParent(dir);
                if (parent is null)
                {
                    break;
                }

                dir = parent.FullName;
            }
        }

        // Return an obviously-invalid path so the next call surfaces a clear "script not found" error.
        return Path.Combine(AppContext.BaseDirectory, "tools", "elk-router", "elk-router.js");
    }
}
