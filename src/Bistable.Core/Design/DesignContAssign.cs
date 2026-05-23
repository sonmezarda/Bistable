namespace Bistable.Core.Design;

public sealed record DesignContAssign(
    string TargetName,
    IReadOnlyList<string> SourceNames,
    string? OperatorSymbol = null,
    DesignBitRange? SourceRange = null);

/// <summary>Inclusive bit range [Hi:Lo] extracted from a wider bus via a Verilog <c>sel</c>.</summary>
public readonly record struct DesignBitRange(int Hi, int Lo)
{
    public int Width => Hi - Lo + 1;
    public override string ToString() => Hi == Lo ? $"[{Hi}]" : $"[{Hi}:{Lo}]";
}
