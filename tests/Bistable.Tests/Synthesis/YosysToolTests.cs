using Bistable.Yosys;

namespace Bistable.Tests.Synthesis;

/// <summary>
/// Phase 6 P6-3 coverage. We can't assume Yosys is installed in CI yet, so
/// these tests cover the failure paths (binary missing, script missing). The
/// happy-path "yosys actually ran the script" is exercised by a separate
/// Integration-tagged test once a CPU synthesis sample lands.
/// </summary>
public sealed class YosysToolTests
{
    [Fact]
    public async Task IsAvailableAsync_ReturnsFalse_WhenBinaryNotOnPath()
    {
        YosysTool tool = new("definitely-not-a-real-binary-99999");
        bool available = await tool.IsAvailableAsync(CancellationToken.None);
        Assert.False(available);
    }

    [Fact]
    public async Task RunScriptAsync_ThrowsFileNotFound_WhenScriptMissing()
    {
        YosysTool tool = new("yosys");
        string missingScript = Path.Combine(Path.GetTempPath(), $"definitely-missing-{Guid.NewGuid():N}.ys");
        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            tool.RunScriptAsync(missingScript, Path.GetTempPath(), CancellationToken.None));
    }
}
