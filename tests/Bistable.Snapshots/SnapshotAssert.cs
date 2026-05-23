using System.Text.Json;
using System.Text.Json.Serialization;
using Bistable.App.Services.Routing.Elk;

namespace Bistable.Snapshots;

// Golden-file snapshot helper. Serializes the actual value deterministically and
// compares against a checked-in golden file. On mismatch writes <name>.actual.json
// next to the golden so reviewers can diff and either fix the code or accept the
// new snapshot.
//
// Regeneration is opt-in via the BISTABLE_REGENERATE_SNAPSHOTS environment
// variable. See docs/TESTING.md Section 4.
public static class SnapshotAssert
{
    private const string RegenerateEnvVar = "BISTABLE_REGENERATE_SNAPSHOTS";

    private static readonly JsonSerializerOptions DefaultSerializer = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        // Preserve a stable property ordering so diffs are meaningful. The ElkGraph
        // model uses default ordering already; for dictionaries inside layoutOptions
        // we sort manually before serializing in the caller.
    };

    public static void MatchesJson(string snapshotName, object actual, JsonSerializerOptions? serializerOptions = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshotName);
        ArgumentNullException.ThrowIfNull(actual);

        string actualJson = JsonSerializer.Serialize(actual, serializerOptions ?? DefaultSerializer).ReplaceLineEndings("\n");
        string goldenDir = ResolveGoldenDirectory();
        string goldenPath = Path.Combine(goldenDir, snapshotName + ".json");
        string actualPath = Path.Combine(goldenDir, snapshotName + ".actual.json");

        if (ShouldRegenerate())
        {
            Directory.CreateDirectory(goldenDir);
            File.WriteAllText(goldenPath, actualJson);
            if (File.Exists(actualPath)) File.Delete(actualPath);
            return;
        }

        if (!File.Exists(goldenPath))
        {
            Directory.CreateDirectory(goldenDir);
            File.WriteAllText(actualPath, actualJson);
            throw new SnapshotException(
                $"Golden snapshot '{snapshotName}' does not exist. Wrote actual to {actualPath}. " +
                $"Inspect it and, if correct, rename to {snapshotName}.json or rerun with " +
                $"{RegenerateEnvVar}=1.");
        }

        string expectedJson = File.ReadAllText(goldenPath).ReplaceLineEndings("\n");
        if (!string.Equals(expectedJson, actualJson, StringComparison.Ordinal))
        {
            File.WriteAllText(actualPath, actualJson);
            throw new SnapshotException(BuildDiffMessage(snapshotName, expectedJson, actualJson, actualPath));
        }

        // Match — clean up any stale .actual.json so a previously-failed run doesn't linger.
        if (File.Exists(actualPath)) File.Delete(actualPath);
    }

    // Convenience for the common case of ELK graphs. Strips position fields (X/Y) so
    // snapshots represent pre-layout structure, not laid-out coordinates that depend
    // on elkjs version.
    public static void MatchesElkGraph(string snapshotName, ElkGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        object snapshot = ElkGraphSnapshotProjector.Project(graph);
        MatchesJson(snapshotName, snapshot);
    }

    private static bool ShouldRegenerate()
    {
        string? value = Environment.GetEnvironmentVariable(RegenerateEnvVar);
        return !string.IsNullOrEmpty(value)
            && (value == "1" || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase));
    }

    // Resolve the golden/ directory by walking up from the test output dir until we
    // find a folder named "Bistable.Snapshots" with a "golden" subdir. This avoids
    // hard-coding repo-relative paths and works whether tests run from bin/Debug or
    // a CI working dir.
    private static string ResolveGoldenDirectory()
    {
        // Prefer co-located golden/ next to the test DLL (csproj copies them with PreserveNewest).
        string baseDir = AppContext.BaseDirectory;
        string colocated = Path.Combine(baseDir, "golden");
        if (Directory.Exists(colocated)) return colocated;

        // Fall back to walking up for the source tree (useful when running from VS).
        DirectoryInfo? dir = new(baseDir);
        while (dir is not null)
        {
            string candidate = Path.Combine(dir.FullName, "tests", "Bistable.Snapshots", "golden");
            if (Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }

        // Last resort: create co-located on first regenerate.
        return colocated;
    }

    private static string BuildDiffMessage(string snapshotName, string expected, string actual, string actualPath)
    {
        string[] expectedLines = expected.Split('\n');
        string[] actualLines = actual.Split('\n');
        int max = Math.Max(expectedLines.Length, actualLines.Length);
        System.Text.StringBuilder sb = new();
        sb.AppendLine($"Snapshot '{snapshotName}' diverged. Actual written to {actualPath}.");
        sb.AppendLine($"To accept: set {RegenerateEnvVar}=1 and rerun, or copy actual.json over golden.json.");
        sb.AppendLine();
        sb.AppendLine("First 30 differing lines (expected vs actual):");
        int shown = 0;
        for (int i = 0; i < max && shown < 30; i++)
        {
            string e = i < expectedLines.Length ? expectedLines[i] : "<missing>";
            string a = i < actualLines.Length ? actualLines[i] : "<missing>";
            if (!string.Equals(e, a, StringComparison.Ordinal))
            {
                sb.AppendLine($"  L{i + 1,4}  - {e}");
                sb.AppendLine($"  L{i + 1,4}  + {a}");
                shown++;
            }
        }
        return sb.ToString();
    }
}

public sealed class SnapshotException : Exception
{
    public SnapshotException(string message) : base(message) { }
}
