namespace Bistable.Core.Synthesis;

/// <summary>
/// Phase 6 P6-4: post-synthesis netlist. Distinct from <c>DesignAst</c> on
/// purpose — RTL AST is "what the user wrote", GateNetlist is "what the
/// synthesizer produced". The two live side-by-side so the GUI can offer
/// both views and a compare flow.
///
/// Wire identity is carried as <see cref="GateBit"/> — each bit on a real
/// net has a unique integer id assigned by the synthesizer. Constants
/// (literal 0 / 1 / x / z) flow as <c>BitKind.Constant*</c> instead of an
/// integer net. Multi-bit ports / cell pins are an ordered list of bits.
/// </summary>
public sealed record GateNetlist(
    string TopModule,
    IReadOnlyDictionary<string, GateModule> Modules);

public sealed record GateModule(
    string Name,
    IReadOnlyList<GatePort> Ports,
    IReadOnlyList<GateCell> Cells,
    IReadOnlyList<GateNet> Nets);

public sealed record GatePort(
    string Name,
    GatePortDirection Direction,
    IReadOnlyList<GateBit> Bits);

public enum GatePortDirection
{
    Input,
    Output,
    InOut,
}

/// <summary>
/// One synthesized cell. <see cref="Type"/> is the library cell name
/// (<c>$_AND_</c>, <c>$_DFF_P_</c>, <c>$_MUX_</c>, …) which the renderer
/// dispatches on to pick the right symbol.
/// </summary>
public sealed record GateCell(
    string Name,
    string Type,
    IReadOnlyDictionary<string, GateConnection> Connections,
    IReadOnlyDictionary<string, GatePortDirection> PortDirections,
    IReadOnlyDictionary<string, string> Parameters,
    IReadOnlyDictionary<string, string> Attributes);

public sealed record GateConnection(
    string PortName,
    IReadOnlyList<GateBit> Bits);

/// <summary>A user-named net. Anonymous nets (those Yosys auto-generates)
/// are NOT included here — only the netnames the user wrote.</summary>
public sealed record GateNet(
    string Name,
    IReadOnlyList<GateBit> Bits);

/// <summary>
/// One bit on a net. Either a numeric net id (<c>NetId &gt;= 2</c>; Yosys
/// reserves 0/1 for constants) or a literal constant flag.
/// </summary>
public readonly record struct GateBit
{
    public BitKind Kind { get; }
    /// <summary>Valid only when <see cref="Kind"/> is <see cref="BitKind.Net"/>.</summary>
    public int NetId { get; }

    private GateBit(BitKind kind, int netId)
    {
        Kind = kind;
        NetId = netId;
    }

    public static GateBit Net(int id) => new(BitKind.Net, id);
    public static readonly GateBit ConstantZero = new(BitKind.ConstantZero, 0);
    public static readonly GateBit ConstantOne  = new(BitKind.ConstantOne,  0);
    public static readonly GateBit ConstantX    = new(BitKind.ConstantX,    0);
    public static readonly GateBit ConstantZ    = new(BitKind.ConstantZ,    0);

    public override string ToString() => Kind switch
    {
        BitKind.Net           => $"net:{NetId}",
        BitKind.ConstantZero  => "0",
        BitKind.ConstantOne   => "1",
        BitKind.ConstantX     => "x",
        BitKind.ConstantZ     => "z",
        _                     => "?",
    };
}

public enum BitKind
{
    Net,
    ConstantZero,
    ConstantOne,
    ConstantX,
    ConstantZ,
}
