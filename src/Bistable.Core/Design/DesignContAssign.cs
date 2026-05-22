namespace Bistable.Core.Design;

public sealed record DesignContAssign(
    string TargetName,
    IReadOnlyList<string> SourceNames);
