namespace Bistable.Verilator;

public sealed class VerilatorInvocationException(
    string operation,
    int exitCode,
    string standardError)
    : InvalidOperationException(
        $"Verilator {operation} failed with exit code {exitCode}.{Environment.NewLine}{standardError}")
{
    public string Operation { get; } = operation;
    public int ExitCode { get; } = exitCode;
    public string StandardError { get; } = standardError;
}
