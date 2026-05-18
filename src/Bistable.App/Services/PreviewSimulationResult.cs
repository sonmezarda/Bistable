namespace Bistable.App.Services;

public sealed record PreviewSimulationResult(bool IsSuccess, string Message)
{
    public static PreviewSimulationResult Success(string message) => new(true, message);

    public static PreviewSimulationResult Failed(string message) => new(false, message);

    public static PreviewSimulationResult Unsupported(string message) => new(false, message);
}
