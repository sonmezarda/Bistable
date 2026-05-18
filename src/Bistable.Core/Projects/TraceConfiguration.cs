namespace Bistable.Core.Projects;

public sealed record TraceConfiguration(bool Enabled = true, string Format = "fst", int Depth = 1);
