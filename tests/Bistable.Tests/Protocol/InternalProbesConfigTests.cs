using Bistable.Core.Projects;

namespace Bistable.Tests.Protocol;

/// <summary>
/// Phase 3 P3-2: ProjectConfiguration.EnableInternalProbes controls whether
/// the Verilator worker is built with the --public-flat-rw flag (which exposes
/// every hierarchical signal as a publicly readable field on the compiled
/// model). Default is true so out-of-the-box probe API works on small/medium
/// designs; large designs can opt out.
///
/// The actual flag plumbing into Verilator's argument list is integration-
/// tested via VerilatorIntegrationTests (which requires Verilator on PATH).
/// These tests cover the config layer and the default behaviour.
/// </summary>
public sealed class InternalProbesConfigTests
{
    [Fact]
    public void Default_EnableInternalProbes_IsTrue()
    {
        // Phase 3's "small designs probe out-of-the-box" promise depends on
        // this default. Changing the default to false would silently break
        // every existing project file's probe API.
        ProjectConfiguration config = new() { TopModule = "top" };

        Assert.True(config.EnableInternalProbes);
    }

    [Fact]
    public void EnableInternalProbes_CanBeDisabledViaInit()
    {
        ProjectConfiguration config = new()
        {
            TopModule = "top",
            EnableInternalProbes = false
        };

        Assert.False(config.EnableInternalProbes);
    }

    [Fact]
    public void ProjectConfiguration_DeserialisesEnableInternalProbes_FromJson()
    {
        // Round-trip through the project file JSON format so existing
        // .bistable.json files (which don't carry the field) still load with
        // the default true, and new files can opt out.
        const string jsonWithoutField = """
            { "topModule": "arnicomp_top" }
            """;
        ProjectConfiguration loaded = System.Text.Json.JsonSerializer
            .Deserialize<ProjectConfiguration>(jsonWithoutField, ProjectConfiguration.JsonOptions)!;
        Assert.True(loaded.EnableInternalProbes);  // default applies

        const string jsonOptedOut = """
            { "topModule": "huge_design", "enableInternalProbes": false }
            """;
        ProjectConfiguration optedOut = System.Text.Json.JsonSerializer
            .Deserialize<ProjectConfiguration>(jsonOptedOut, ProjectConfiguration.JsonOptions)!;
        Assert.False(optedOut.EnableInternalProbes);
    }
}
