using System.Diagnostics;
using Bistable.App.Services;

namespace Bistable.Tests;

public sealed class LiveReloadPerformanceTests
{
    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Category", "Timing")]
    public async Task RiscvSingleCycle_XmlElaboration_CompletesWithinTwoSeconds()
    {
        string root = FindRepositoryRoot();
        string projectPath = Path.Combine(root, "samples", "riscv_single_cycle", "riscv_single_cycle.bistable.json");
        Stopwatch stopwatch = Stopwatch.StartNew();

        DesignLoadResult result = await new DesignLoadService().LoadAsync(projectPath, CancellationToken.None);
        stopwatch.Stop();

        Assert.NotNull(result.Ast);
        Assert.True(
            stopwatch.Elapsed <= TimeSpan.FromSeconds(2),
            $"riscv_single_cycle elaboration took {stopwatch.Elapsed.TotalMilliseconds:F0} ms; budget is 2000 ms.");
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Bistable.slnx")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName ?? throw new InvalidOperationException("Repository root could not be found.");
    }
}
