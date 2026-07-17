namespace Bistable.Core.Projects;

public sealed class ProjectConfigurationValidator
{
    public IReadOnlyList<string> Validate(ProjectConfiguration configuration, string? baseDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        List<string> errors = [];

        if (string.IsNullOrWhiteSpace(configuration.TopModule))
        {
            errors.Add("Top module is required.");
        }

        if (configuration.Sources.Count == 0)
        {
            errors.Add("At least one SystemVerilog source file is required.");
        }

        string root = string.IsNullOrWhiteSpace(baseDirectory)
            ? Directory.GetCurrentDirectory()
            : baseDirectory;

        foreach (string source in configuration.Sources)
        {
            if (string.IsNullOrWhiteSpace(source))
            {
                errors.Add("Source file path cannot be empty.");
                continue;
            }

            string fullPath = Path.IsPathRooted(source) ? source : Path.GetFullPath(source, root);
            if (!File.Exists(fullPath))
            {
                errors.Add($"Source file does not exist: {source}");
            }
        }

        foreach (ClockHint clock in configuration.Clocks)
        {
            if (clock.DefaultPeriodNs <= 0)
            {
                errors.Add($"Clock '{clock.Name}' period must be greater than zero.");
            }
        }

        if (configuration.Trace.Enabled)
        {
            if (!string.Equals(configuration.Trace.Format, "vcd", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"Trace format '{configuration.Trace.Format}' is not supported yet. Use 'vcd'.");
            }

            if (configuration.Trace.Depth <= 0)
            {
                errors.Add("Trace depth must be greater than zero when tracing is enabled.");
            }
        }

        if (configuration.LiveReload.DebounceMs is < 100 or > 5000)
        {
            errors.Add("Live reload debounce must be between 100 and 5000 milliseconds.");
        }

        return errors;
    }
}
