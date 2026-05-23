using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bistable.App.Services.Routing.Elk;

/// <summary>
/// Long-lived bridge to the elk-router Node.js process.
/// The process is started on first use and reused across layout calls,
/// avoiding the ~150 ms Node.js startup overhead per request.
/// </summary>
public sealed class ElkRunner : IDisposable
{
    private static readonly TimeSpan DefaultResponseTimeout = TimeSpan.FromSeconds(8);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private readonly string _nodeExecutable;
    private readonly string _scriptPath;
    private readonly TimeSpan _responseTimeout;
    private readonly Lock _lock = new();

    private Process? _process;
    private bool _disposed;

    public ElkRunner()
        : this("node", ResolveDefaultScriptPath(), DefaultResponseTimeout)
    {
    }

    public ElkRunner(string nodeExecutable, string scriptPath, TimeSpan responseTimeout)
    {
        _nodeExecutable = nodeExecutable;
        _scriptPath = scriptPath;
        _responseTimeout = responseTimeout;
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

        // A single newline terminates the request line.
        if (requestJson.Contains('\n'))
        {
            throw new SchematicRoutingException("ELK request JSON must not contain embedded newlines.");
        }

        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            string responseLine = InvokeOnPersistentProcess(requestJson);
            return ParseResponse(responseLine);
        }
    }

    /// <summary>Returns the serialized ELK request JSON. Useful for diagnostics.</summary>
    public static string SerializeForDebug(ElkGraph input) => JsonSerializer.Serialize(input, JsonOptions);

    public void Dispose()
    {
        lock (_lock)
        {
            _disposed = true;
            StopProcess();
        }
    }

    private string InvokeOnPersistentProcess(string requestJson)
    {
        EnsureProcessRunning();

        try
        {
            _process!.StandardInput.WriteLine(requestJson);
            _process.StandardInput.Flush();
        }
        catch (Exception ex)
        {
            StopProcess();
            throw new SchematicRoutingException($"Failed to write to ELK router process: {ex.Message}", ex);
        }

        Task<string?> readTask = _process.StandardOutput.ReadLineAsync();
        if (!readTask.Wait(_responseTimeout))
        {
            StopProcess();
            throw new SchematicRoutingException($"ELK routing timed out after {_responseTimeout.TotalSeconds:0.#} seconds.");
        }

        string? line = readTask.Result;
        if (string.IsNullOrWhiteSpace(line))
        {
            // Process likely died — clear it so next call restarts.
            string stderr = TryReadStderr();
            StopProcess();
            throw new SchematicRoutingException(
                string.IsNullOrWhiteSpace(stderr)
                    ? "ELK router returned an empty response."
                    : $"ELK router returned an empty response. stderr: {stderr.Trim()}");
        }

        return line;
    }

    private static ElkGraph ParseResponse(string responseLine)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(responseLine);
            JsonElement root = doc.RootElement;
            bool ok = root.TryGetProperty("ok", out JsonElement okProp) && okProp.GetBoolean();
            if (!ok)
            {
                string errorMsg = root.TryGetProperty("error", out JsonElement errProp)
                    ? errProp.GetString() ?? "unknown ELK error"
                    : "ELK router reported failure without an error message.";
                throw new SchematicRoutingException($"ELK layout failed: {errorMsg}");
            }

            if (!root.TryGetProperty("graph", out JsonElement graphProp))
            {
                throw new SchematicRoutingException("ELK response missing 'graph' field.");
            }

            string graphJson = graphProp.GetRawText();
            ElkGraph? result = JsonSerializer.Deserialize<ElkGraph>(graphJson, JsonOptions);
            return result ?? throw new SchematicRoutingException("ELK returned a null layout.");
        }
        catch (JsonException ex)
        {
            string snippet = responseLine.Length > 200 ? responseLine[..200] + "…" : responseLine;
            throw new SchematicRoutingException(
                $"ELK returned invalid JSON ({ex.Message}). Response head: {snippet}", ex);
        }
    }

    private void EnsureProcessRunning()
    {
        if (_process is not null && !_process.HasExited)
        {
            return;
        }

        StopProcess();

        if (!File.Exists(_scriptPath))
        {
            throw new SchematicRoutingException(
                $"ELK router script not found at '{_scriptPath}'. Run 'npm install' inside tools/elk-router/.");
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
            _process = Process.Start(startInfo)
                ?? throw new SchematicRoutingException("Failed to start the ELK router process.");
        }
        catch (Win32Exception ex)
        {
            throw new SchematicRoutingException(
                $"Node.js executable '{_nodeExecutable}' not found. Install Node.js (>= 18) to enable the ELK router.", ex);
        }
    }

    private string TryReadStderr()
    {
        if (_process is null) return string.Empty;
        try
        {
            // Non-blocking peek: only read what's already buffered.
            _process.StandardError.BaseStream.ReadTimeout = 50;
            return _process.StandardError.ReadToEnd();
        }
        catch
        {
            return string.Empty;
        }
    }

    private void StopProcess()
    {
        if (_process is null) return;
        try
        {
            if (!_process.HasExited)
            {
                _process.StandardInput.Close();
                if (!_process.WaitForExit(500))
                {
                    _process.Kill(entireProcessTree: true);
                }
            }
        }
        catch (InvalidOperationException)
        {
            // Process exited in the race window between HasExited check and Kill; nothing to terminate.
        }
        finally
        {
            _process.Dispose();
            _process = null;
        }
    }

    private static string ResolveDefaultScriptPath()
    {
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
                if (parent is null) break;
                dir = parent.FullName;
            }
        }

        return Path.Combine(AppContext.BaseDirectory, "tools", "elk-router", "elk-router.js");
    }
}
