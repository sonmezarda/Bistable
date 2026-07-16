using System.Text.Json.Serialization;

namespace Bistable.Core.Design.Ast;

public sealed record CombinationalBlockAst(StatementAst Body)
{
    /// <summary>
    /// Results produced by <c>CombinationalProjector</c>. A null value means the
    /// block has not passed through the projector yet; an empty list means it was
    /// processed and contained no assignment targets.
    /// </summary>
    [JsonIgnore]
    public IReadOnlyList<CombinationalProjectionTarget>? ProjectionResults { get; init; }
}

public sealed record CombinationalProjectionTarget(
    int TargetIndex,
    LValueAst Target,
    string SignalName,
    CombinationalProjectionStatus Status,
    string Reason,
    IReadOnlyList<string> ReadSignals,
    int? SyntheticContAssignIndex);

public enum CombinationalProjectionStatus
{
    Projected,
    Unsupported,
}
