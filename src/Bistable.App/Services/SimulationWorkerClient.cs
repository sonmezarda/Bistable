using System.Diagnostics;
using Bistable.Protocol;

namespace Bistable.App.Services;

public sealed class SimulationWorkerClient : IAsyncDisposable
{
    private readonly Process _process;

    public SimulationWorkerClient(string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);

        _process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                WorkingDirectory = Path.GetDirectoryName(executablePath) ?? Directory.GetCurrentDirectory(),
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            }
        };

        if (!_process.Start())
        {
            throw new InvalidOperationException($"Failed to start simulation worker: {executablePath}");
        }
    }

    public async Task<SimulationSnapshot> SendAsync(SimulationCommand command, CancellationToken cancellationToken)
    {
        if (_process.HasExited)
        {
            throw new InvalidOperationException($"Simulation worker exited with code {_process.ExitCode}.");
        }

        await _process.StandardInput.WriteLineAsync(ProtocolJson.Serialize(command).AsMemory(), cancellationToken);
        await _process.StandardInput.FlushAsync(cancellationToken);

        string? line = await _process.StandardOutput.ReadLineAsync(cancellationToken);
        if (line is null)
        {
            string stderr = await _process.StandardError.ReadToEndAsync(cancellationToken);
            throw new InvalidOperationException($"Simulation worker closed stdout. {stderr}");
        }

        return ProtocolJson.Deserialize<SimulationSnapshot>(line)
            ?? throw new InvalidDataException("Simulation worker returned an invalid snapshot.");
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (!_process.HasExited)
            {
                await _process.StandardInput.WriteLineAsync(ProtocolJson.Serialize(new SimulationCommand(SimulationCommandType.Pause)));
                _process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
        finally
        {
            _process.Dispose();
        }
    }
}
