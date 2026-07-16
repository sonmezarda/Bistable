namespace Bistable.Protocol;

/// <summary>Version and capability names shared by the GUI and generated worker.</summary>
public static class WorkerProtocol
{
    public const int CurrentVersion = 3;
    public const int MaxSignalsPerBatch = 4096;
    public const string ReadSignalsCapability = "readSignals";
}
