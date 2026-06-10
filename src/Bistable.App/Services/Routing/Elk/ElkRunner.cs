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
public sealed class ElkRunner : IElkRunner
{
    // Phase 6.5 Wave 5: the user-facing timeout is no longer a 45 s kill
    // switch. Gate-level windows run through SchematicLayoutService, which
    // raises a soft warning at 10 s and lets the user cancel. This runner keeps
    // only a 10-minute sanity cap for a truly wedged Node/ELK process.
    private static readonly TimeSpan DefaultResponseTimeout = TimeSpan.FromMinutes(10);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    private static int s_dumpSequence;

    private readonly string _nodeExecutable;
    private readonly string _scriptPath;
    private readonly TimeSpan _responseTimeout;
    private readonly Lock _stateLock = new();
    private readonly Lock _requestLock = new();

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

        lock (_requestLock)
        {
            Process process = GetOrStartProcess();
            int dumpId = TryDumpJson("request", requestJson);
            string responseLine = InvokeOnPersistentProcess(process, requestJson);
            if (dumpId > 0)
            {
                TryDumpJson("response", responseLine, dumpId);
            }
            return ParseResponse(responseLine);
        }
    }

    /// <summary>Returns the serialized ELK request JSON. Useful for diagnostics.</summary>
    public static string SerializeForDebug(ElkGraph input) => JsonSerializer.Serialize(input, JsonOptions);

    private static int TryDumpJson(string kind, string json, int? existingDumpId = null)
    {
        string? dumpDir = Environment.GetEnvironmentVariable("BISTABLE_ELK_DUMP_DIR");
        if (string.IsNullOrWhiteSpace(dumpDir))
        {
            return 0;
        }

        try
        {
            Directory.CreateDirectory(dumpDir);
            int dumpId = existingDumpId ?? Interlocked.Increment(ref s_dumpSequence);
            string path = Path.Combine(dumpDir, $"elk-{dumpId:0000}-{kind}.json");
            File.WriteAllText(path, json, Utf8NoBom);
            return dumpId;
        }
        catch
        {
            // Diagnostics must never break schematic rendering.
            return 0;
        }
    }

    public void Dispose()
    {
        Process? process;
        lock (_stateLock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            process = DetachProcess();
        }
        StopProcess(process, force: true);
    }

    public void Restart()
    {
        Process? process;
        lock (_stateLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            process = DetachProcess();
        }
        StopProcess(process, force: true);
        _ = GetOrStartProcess();
    }

    private string InvokeOnPersistentProcess(Process process, string requestJson)
    {
        try
        {
            process.StandardInput.WriteLine(requestJson);
            process.StandardInput.Flush();
        }
        catch (Exception ex)
        {
            InvalidateProcess(process);
            throw new SchematicRoutingException($"Failed to write to ELK router process: {ex.Message}", ex);
        }

        Task<string?> readTask = process.StandardOutput.ReadLineAsync();
        bool responseCompleted;
        try
        {
            responseCompleted = readTask.Wait(_responseTimeout);
        }
        catch (AggregateException ex) when (
            ex.InnerException is IOException or ObjectDisposedException or InvalidOperationException)
        {
            InvalidateProcess(process);
            throw new SchematicRoutingException("ELK router process was interrupted.", ex.InnerException);
        }

        if (!responseCompleted)
        {
            InvalidateProcess(process);
            throw new SchematicRoutingException($"ELK routing timed out after {_responseTimeout.TotalSeconds:0.#} seconds.");
        }

        string? line;
        try
        {
            line = readTask.GetAwaiter().GetResult();
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
        {
            InvalidateProcess(process);
            throw new SchematicRoutingException("ELK router process was interrupted.", ex);
        }

        if (string.IsNullOrWhiteSpace(line))
        {
            // Process likely died — clear it so next call restarts.
            string stderr = TryReadStderr(process);
            InvalidateProcess(process);
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

    private Process GetOrStartProcess()
    {
        lock (_stateLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_process is not null && !_process.HasExited)
            {
                return _process;
            }

            Process? staleProcess = DetachProcess();
            StopProcess(staleProcess, force: true);

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
                return _process;
            }
            catch (Win32Exception ex)
            {
                throw new SchematicRoutingException(
                    $"Node.js executable '{_nodeExecutable}' not found. Install Node.js (>= 18) to enable the ELK router.", ex);
            }
        }
    }

    private static string TryReadStderr(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                return string.Empty;
            }
            return process.StandardError.ReadToEnd();
        }
        catch
        {
            return string.Empty;
        }
    }

    private void InvalidateProcess(Process process)
    {
        lock (_stateLock)
        {
            if (ReferenceEquals(_process, process))
            {
                _process = null;
            }
        }
        StopProcess(process, force: true);
    }

    private Process? DetachProcess()
    {
        Process? process = _process;
        _process = null;
        return process;
    }

    private static void StopProcess(Process? process, bool force)
    {
        if (process is null) return;
        try
        {
            if (!process.HasExited)
            {
                if (force)
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(250);
                }
                else
                {
                    process.StandardInput.Close();
                    if (!process.WaitForExit(250))
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
            }
        }
        catch (Exception ex) when (
            ex is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            // Process exited in the race window or the platform could not kill
            // the complete tree. Try the direct process as a final fallback.
            try
            {
                if (!process.HasExited)
                {
                    process.Kill();
                }
            }
            catch
            {
                // Best effort teardown. The process handle is still disposed
                // below and the next request starts a fresh router.
            }
        }
        finally
        {
            process.Dispose();
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
