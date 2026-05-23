namespace Bistable.Core.Design.Ast;

public readonly record struct BitRange(int Hi, int Lo)
{
    public int Width => Hi - Lo + 1;
    public override string ToString() => Hi == Lo ? $"[{Hi}]" : $"[{Hi}:{Lo}]";
}
