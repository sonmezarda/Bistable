namespace Bistable.Core.Projects;

public sealed record LiveReloadConfiguration(
    bool Enabled = true,
    int DebounceMs = 400);
