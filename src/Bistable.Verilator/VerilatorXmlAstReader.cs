using System.Globalization;
using System.Numerics;
using System.Xml.Linq;
using Bistable.Core.Design;
using Bistable.Core.Design.Ast;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bistable.Verilator;

/// <summary>
/// Parses a Verilator-generated XML file into the backend-agnostic <see cref="DesignAst"/>.
/// The AST uses no Verilator-specific names; a future Yosys reader would emit the same types.
/// </summary>
public sealed class VerilatorXmlAstReader
{
    private const int MaxExpressionDepth = 200;

    private readonly ILogger<VerilatorXmlAstReader> _logger;

    public VerilatorXmlAstReader(ILogger<VerilatorXmlAstReader>? logger = null)
    {
        _logger = logger ?? NullLogger<VerilatorXmlAstReader>.Instance;
    }

    public DesignAst Read(string xmlPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(xmlPath);
        XDocument doc = XDocument.Load(xmlPath, LoadOptions.None);
        return ParseDesign(doc);
    }

    // ── Root ────────────────────────────────────────────────────────────────

    private DesignAst ParseDesign(XDocument doc)
    {
        List<XElement> moduleElements = doc.Descendants("module").ToList();
        if (moduleElements.Count == 0)
            throw new InvalidDataException("Verilator XML <netlist> contains zero <module> elements.");

        Dictionary<string, DType> dtypes = BuildDTypeMap(doc);
        // P2-11: resolve packed-struct definitions from <typetable>. Keyed by Verilator
        // dtype_id so a signal whose dtype is a struct can attach the metadata.
        Dictionary<string, StructTypeDecl> structTypes = BuildStructTypeMap(doc, dtypes);

        List<ModuleAst> modules = moduleElements
            .Select(e => ParseModule(e, dtypes, structTypes))
            .ToList();

        modules = ComputeIsRegistered(modules);

        return TempFolder.Fold(new DesignAst(modules));
    }

    // ── Struct type map (P2-11) ─────────────────────────────────────────────

    // Parses <structdtype id="..." name="..."> entries inside the <typetable>.
    // Members are listed in declaration order (top-of-struct first). In SystemVerilog
    // packed structs the first declared field is the MSB-end of the bus, so we
    // accumulate widths MSB-first to compute each field's (lo, width).
    private static Dictionary<string, StructTypeDecl> BuildStructTypeMap(XDocument doc, Dictionary<string, DType> dtypes)
    {
        Dictionary<string, StructTypeDecl> result = new(StringComparer.Ordinal);

        foreach (XElement structElement in doc.Descendants("structdtype"))
        {
            StructTypeDecl? decl = ParseStructType(structElement, dtypes);
            if (decl is not null && (string?)structElement.Attribute("id") is string id)
                result[id] = decl;
        }

        // <refdtype id="58" sub_dtype_id="5"/> means dtype 58 is an alias for the struct
        // at id 5. Register both ids so signals using either resolve correctly.
        foreach (XElement refElement in doc.Descendants("refdtype"))
        {
            string? refId = (string?)refElement.Attribute("id");
            string? subId = (string?)refElement.Attribute("sub_dtype_id");
            if (refId is not null && subId is not null &&
                result.TryGetValue(subId, out StructTypeDecl? resolved))
            {
                result[refId] = resolved;
            }
        }

        return result;
    }

    private static StructTypeDecl? ParseStructType(XElement structElement, Dictionary<string, DType> dtypes)
    {
        if ((string?)structElement.Attribute("id") is null) return null;
        string name = (string?)structElement.Attribute("name") ?? "<anonymous>";

        // Members are declared MSB-first (top of struct = high bits). We collect
        // (name, width) in declaration order, then assign (Lo, Width) by walking
        // backward from end to start so the last-declared field sits at Lo=0.
        List<(string FieldName, int Width)> members = structElement.Elements("memberdtype")
            .Select(m => (
                FieldName: (string?)m.Attribute("name") ?? "?",
                Width: ResolveMemberWidth((string?)m.Attribute("sub_dtype_id"), dtypes)))
            .ToList();

        int totalWidth = members.Sum(static m => m.Width);
        List<StructFieldDecl> fields = new(members.Count);
        int lo = 0;
        for (int i = members.Count - 1; i >= 0; i--)
        {
            (string fieldName, int width) = members[i];
            fields.Add(new StructFieldDecl(fieldName, lo, width));
            lo += width;
        }
        fields.Reverse();
        return new StructTypeDecl(name, totalWidth, fields);
    }

    private static int ResolveMemberWidth(string? subDtypeId, Dictionary<string, DType> dtypes)
    {
        return subDtypeId is not null && dtypes.TryGetValue(subDtypeId, out DType? sub) ? sub.Width : 1;
    }

    // ── DType map ───────────────────────────────────────────────────────────

    private static Dictionary<string, DType> BuildDTypeMap(XDocument doc)
    {
        // First pass: parse every dtype with its raw width.
        Dictionary<string, DType> map = doc.Descendants()
              .Where(static e => e.Name.LocalName.EndsWith("dtype", StringComparison.Ordinal))
              .Select(ParseDType)
              .Where(static d => d.Id is not null)
              .ToDictionary(static d => d.Id!, static d => d);

        // P3-6: a second pass to resolve unpackarraydtype's CELL width by
        // following sub_dtype_id. Without this, `logic [7:0] mem [0:15]`
        // ends up reporting Width=1 (the unpackarraydtype itself carries no
        // basic width) and the memory probe enumerator can't compute the
        // cell width correctly.
        foreach (XElement e in doc.Descendants()
                     .Where(static x => x.Name.LocalName.Equals("unpackarraydtype", StringComparison.Ordinal)))
        {
            string? id = (string?)e.Attribute("id");
            string? subId = (string?)e.Attribute("sub_dtype_id");
            if (id is null || subId is null) continue;
            if (!map.TryGetValue(id, out DType? current)) continue;
            if (!map.TryGetValue(subId, out DType? sub)) continue;
            map[id] = current with { Width = sub.Width, IsSigned = sub.IsSigned };
        }
        return map;
    }

    private static DType ParseDType(XElement e)
    {
        string? id = (string?)e.Attribute("id");
        int left = ParseInt((string?)e.Attribute("left"), 0);
        int right = ParseInt((string?)e.Attribute("right"), 0);
        bool hasRange = e.Attribute("left") is not null && e.Attribute("right") is not null;
        int width = hasRange ? Math.Abs(left - right) + 1 : 1;
        bool signed = string.Equals((string?)e.Attribute("signed"), "true", StringComparison.Ordinal);
        IReadOnlyList<BitRange> arrayDims = ParseArrayDims(e);
        return new DType(id, Math.Max(1, width), signed, arrayDims);
    }

    private static IReadOnlyList<BitRange> ParseArrayDims(XElement dtypeElement)
    {
        // unpackarraydtype carries its bounds as a nested <range> with two
        // <const> children whose `name` attribute encodes the literal
        // (e.g. "32'sh0", "32'shf"). Some Verilator versions also emit
        // top-level `left`/`right` attributes — handle both.
        if (!dtypeElement.Name.LocalName.Equals("unpackarraydtype", StringComparison.Ordinal))
            return [];

        int left;
        int right;
        XElement? range = dtypeElement.Element("range");
        if (range is not null)
        {
            XElement[] consts = range.Elements("const").ToArray();
            left = consts.Length > 0 ? ParseConstNameValue(consts[0]) : 0;
            right = consts.Length > 1 ? ParseConstNameValue(consts[1]) : 0;
        }
        else
        {
            left = ParseInt((string?)dtypeElement.Attribute("left"), 0);
            right = ParseInt((string?)dtypeElement.Attribute("right"), 0);
        }
        int hi = Math.Max(left, right);
        int lo = Math.Min(left, right);
        return [new BitRange(hi, lo)];
    }

    /// <summary>
    /// Verilator's <c>&lt;const name="32'sh0"/&gt;</c> form: parse the literal
    /// part after the apostrophe. Handles <c>'sh</c>, <c>'sd</c>, <c>'h</c>,
    /// <c>'d</c>, <c>'b</c> and a couple of common quirks.
    /// </summary>
    private static int ParseConstNameValue(XElement constElement)
    {
        string? name = (string?)constElement.Attribute("name");
        if (string.IsNullOrEmpty(name)) return 0;
        // Decoded entity already by XLinq, but be defensive.
        name = name.Replace("&apos;", "'", StringComparison.Ordinal);
        int apos = name.IndexOf('\'');
        if (apos < 0) return ParseInt(name, 0);
        string suffix = name[(apos + 1)..];
        if (suffix.Length < 2) return 0;
        char baseChar = char.ToLowerInvariant(suffix[suffix.StartsWith("s", StringComparison.OrdinalIgnoreCase) ? 1 : 0]);
        string digits = suffix[(suffix.StartsWith("s", StringComparison.OrdinalIgnoreCase) ? 2 : 1)..];
        int radix = baseChar switch { 'h' => 16, 'd' => 10, 'b' => 2, 'o' => 8, _ => 10 };
        try { return Convert.ToInt32(digits, radix); }
        catch (FormatException) { return 0; }
        catch (OverflowException) { return 0; }
    }

    // ── Module ──────────────────────────────────────────────────────────────

    private ModuleAst ParseModule(XElement e, Dictionary<string, DType> dtypes, Dictionary<string, StructTypeDecl> structTypes)
    {
        string name = (string?)e.Attribute("name") ?? "unknown";
        bool isTop = string.Equals((string?)e.Attribute("topModule"), "1", StringComparison.Ordinal);

        List<PortDecl> ports = e.Elements("var")
            .Where(static v => v.Attribute("dir") is not null)
            .Select(v => ParsePortDecl(v, dtypes))
            .OrderBy(static p => p.PinIndex)
            .ToList();

        List<DesignParameter> parameters = e.Elements("var")
            .Where(static v => string.Equals((string?)v.Attribute("param"), "true", StringComparison.Ordinal))
            .Select(ParseParameter)
            .ToList();

        List<SignalDecl> locals = e.Elements("var")
            .Where(static v => v.Attribute("dir") is null &&
                               !string.Equals((string?)v.Attribute("param"), "true", StringComparison.Ordinal))
            .Select(v => ParseSignalDecl(v, dtypes, structTypes))
            .ToList();

        List<InstanceDecl> instances = e.Elements("instance")
            .Select(ParseInstanceDecl)
            .ToList();

        List<ContAssignAst> contAssigns = e.Elements("contassign")
            .Select(ParseContAssign)
            .OfType<ContAssignAst>()
            .ToList();

        List<SequentialBlockAst> sequential = [];
        List<CombinationalBlockAst> combinational = [];

        foreach (XElement always in e.Elements("always"))
        {
            if (always.Element("sentree") is { } sentree)
                sequential.Add(ParseSequentialBlock(always, sentree));
            else
                combinational.Add(new CombinationalBlockAst(ParseAlwaysBody(always)));
        }

        return new ModuleAst(name, isTop, ports, parameters, locals,
                             instances, contAssigns, sequential, combinational);
    }

    // ── Port / Signal / Parameter ────────────────────────────────────────────

    private static PortDecl ParsePortDecl(XElement e, Dictionary<string, DType> dtypes)
    {
        string name = Attr(e, "name");
        DType dtype = LookupDType(dtypes, (string?)e.Attribute("dtype_id"));
        SignalDirection dir = ParseDirection((string?)e.Attribute("dir") ?? string.Empty);
        int pin = ParseInt((string?)e.Attribute("pinIndex"), 0);
        return new PortDecl(name, dir, dtype.Width, dtype.IsSigned, pin);
    }

    private static SignalDecl ParseSignalDecl(XElement e, Dictionary<string, DType> dtypes, Dictionary<string, StructTypeDecl> structTypes)
    {
        string name = Attr(e, "name");
        string? dtypeId = (string?)e.Attribute("dtype_id");
        DType dtype = LookupDType(dtypes, dtypeId);
        // P2-11: when the signal's dtype is a packed struct, attach the resolved
        // metadata so the schematic decoder can emit per-field fan-out.
        StructTypeDecl? structType = dtypeId is not null && structTypes.TryGetValue(dtypeId, out StructTypeDecl? s) ? s : null;
        int width = structType?.TotalWidth ?? dtype.Width;
        return new SignalDecl(name, width, dtype.IsSigned, dtype.ArrayDims, IsRegistered: false, StructType: structType);
    }

    private static DesignParameter ParseParameter(XElement e)
    {
        string name = Attr(e, "name");
        string value = (string?)e.Element("const")?.Attribute("name") ?? string.Empty;
        return new DesignParameter(name, value);
    }

    // ── Instance ────────────────────────────────────────────────────────────

    private static InstanceDecl ParseInstanceDecl(XElement e)
    {
        string instanceName = Attr(e, "name");
        string moduleName = Attr(e, "defName");
        List<PortConnectionDecl> connections = e.Elements("port")
            .Select(ParsePortConnectionDecl)
            .OrderBy(static c => c.PortIndex)
            .ToList();
        return new InstanceDecl(instanceName, moduleName, connections);
    }

    private static PortConnectionDecl ParsePortConnectionDecl(XElement e)
    {
        string portName = Attr(e, "name");
        string direction = (string?)e.Attribute("direction") ?? string.Empty;
        int portIndex = ParseInt((string?)e.Attribute("portIndex"), 0);

        // P4.5: concat-bundled pin (e.g. `.d({a, b, c})`). Verilator nests these
        // right-associatively, so flatten all descendant <varref>s in document
        // order — that matches MSB-first concat layout.
        XElement? concatWrapper = e.Element("concat");
        if (concatWrapper is not null)
        {
            List<string> parts = concatWrapper
                .Descendants("varref")
                .Select(static v => (string?)v.Attribute("name"))
                .Where(static n => !string.IsNullOrEmpty(n))
                .Cast<string>()
                .ToList();
            if (parts.Count > 0)
            {
                return new PortConnectionDecl(portName, "?", direction, portIndex, SignalRange: null, ConcatParts: parts);
            }
        }

        // Signal name extraction: direct varref > sel-wrapped varref > const literal > "?"
        XElement? selWrapper = e.Element("sel");
        string? signalName =
            (string?)e.Element("varref")?.Attribute("name")
            ?? (string?)selWrapper?.Element("varref")?.Attribute("name")
            ?? (string?)e.Element("const")?.Attribute("name")
            ?? "?";

        // P2-11: when the connection is wrapped in <sel>, capture the slice range so
        // the decoder can detect struct field accesses (e.g. control_pins.ops).
        BitRange? signalRange = ExtractSelRange(selWrapper);

        return new PortConnectionDecl(portName, signalName, direction, portIndex, signalRange);
    }

    // Returns the [lo, width] range encoded by a <sel> wrapper's two <const> children,
    // or null when the sel is missing/malformed.
    private static BitRange? ExtractSelRange(XElement? selWrapper)
    {
        if (selWrapper is null) return null;
        List<XElement> consts = selWrapper.Elements("const").ToList();
        if (consts.Count < 2) return null;
        int lo = ParseVerilogConst(consts[0]);
        int width = Math.Max(1, ParseVerilogConst(consts[1]));
        return new BitRange(lo + width - 1, lo);
    }

    // ── ContAssign ──────────────────────────────────────────────────────────

    private ContAssignAst? ParseContAssign(XElement e)
    {
        // Children: [RHS expression, ..., LHS varref/lvalue]
        // The LHS target is the last direct <varref> child (or a structured lvalue).
        List<XElement> children = e.Elements().ToList();
        if (children.Count == 0)
            return null;

        XElement lhsElement = children[^1];
        LValueAst target = ParseLValue(lhsElement);

        // RHS: everything except the final element
        XElement? rhsElement = children.Count > 1 ? children[0] : null;
        ExpressionAst source = rhsElement is not null
            ? ParseExpression(rhsElement)
            : new ConstExpr(BigInteger.Zero, 1, false);

        return new ContAssignAst(target, source);
    }

    // ── Sequential block ────────────────────────────────────────────────────

    private SequentialBlockAst ParseSequentialBlock(XElement always, XElement sentree)
    {
        List<EdgeTrigger> triggers = sentree.Elements("senitem")
            .Select(ParseEdgeTrigger)
            .ToList();

        bool asyncReset = triggers.Any(static t =>
            t.Edge == EdgeKind.Falling &&
            (t.SignalName.Contains("rst", StringComparison.OrdinalIgnoreCase) ||
             t.SignalName.Contains("reset", StringComparison.OrdinalIgnoreCase)));

        StatementAst body = ParseAlwaysBody(always);
        return new SequentialBlockAst(triggers, body, asyncReset);
    }

    private StatementAst ParseAlwaysBody(XElement always)
    {
        // Body is the first non-sentree child of <always>
        XElement? bodyElement = always.Elements()
            .FirstOrDefault(static e => !e.Name.LocalName.Equals("sentree", StringComparison.Ordinal));
        return bodyElement is not null ? ParseStatement(bodyElement) : new BeginAst([]);
    }

    private static EdgeTrigger ParseEdgeTrigger(XElement senitem)
    {
        string signalName = (string?)senitem.Element("varref")?.Attribute("name") ?? string.Empty;
        EdgeKind edge = ((string?)senitem.Attribute("edgeType"))?.ToUpperInvariant() switch
        {
            "POS"  => EdgeKind.Rising,
            "NEG"  => EdgeKind.Falling,
            "BOTH" => EdgeKind.AnyChange,
            _      => EdgeKind.AnyChange
        };
        return new EdgeTrigger(edge, signalName);
    }

    // ── Statement ───────────────────────────────────────────────────────────

    private StatementAst ParseStatement(XElement e)
    {
        return e.Name.LocalName switch
        {
            "begin"    => ParseBegin(e),
            "if"       => ParseIf(e),
            "case"     or "casestmt" => ParseCase(e),
            "assign"   => ParseAssignStatement(e, isNonBlocking: false),
            "assigndly" => ParseAssignStatement(e, isNonBlocking: true),
            _ => SkipStatement(e)
        };
    }

    private BeginAst ParseBegin(XElement e)
    {
        List<StatementAst> stmts = e.Elements().Select(ParseStatement).ToList();
        return new BeginAst(stmts);
    }

    private IfAst ParseIf(XElement e)
    {
        List<XElement> children = e.Elements().ToList();
        ExpressionAst condition = children.Count > 0 ? ParseExpression(children[0]) : new ConstExpr(BigInteger.Zero, 1, false);
        StatementAst then = children.Count > 1 ? ParseStatement(children[1]) : new BeginAst([]);
        StatementAst? elseB = children.Count > 2 ? ParseStatement(children[2]) : null;
        return new IfAst(condition, then, elseB);
    }

    private CaseAst ParseCase(XElement e)
    {
        List<XElement> children = e.Elements().ToList();
        ExpressionAst subject = children.Count > 0 ? ParseExpression(children[0]) : new ConstExpr(BigInteger.Zero, 1, false);

        List<CaseArm> arms = [];
        StatementAst? defaultArm = null;

        foreach (XElement item in children.Skip(1))
        {
            if (!item.Name.LocalName.Equals("caseitem", StringComparison.Ordinal))
                continue;

            List<XElement> itemChildren = item.Elements().ToList();
            if (itemChildren.Count == 0)
                continue;

            // A <caseitem> whose first child is a statement element (not an expr) is the default arm.
            // Heuristic: if count == 1, it's default (just the body). If > 1, first is label, last is body.
            if (itemChildren.Count == 1)
            {
                defaultArm = ParseStatement(itemChildren[0]);
            }
            else
            {
                ExpressionAst label = ParseExpression(itemChildren[0]);
                StatementAst body = ParseStatement(itemChildren[^1]);
                arms.Add(new CaseArm(label, body));
            }
        }

        return new CaseAst(subject, arms, defaultArm);
    }

    private AssignAst ParseAssignStatement(XElement e, bool isNonBlocking)
    {
        List<XElement> children = e.Elements().ToList();
        if (children.Count < 2)
        {
            _logger.LogWarning("Skipping malformed {Name} element with fewer than 2 children.", e.Name.LocalName);
            return new AssignAst(new VarRefLValue("__unknown__"), new ConstExpr(BigInteger.Zero, 1, false), isNonBlocking);
        }

        ExpressionAst source = ParseExpression(children[0]);
        LValueAst target = ParseLValue(children[^1]);
        return new AssignAst(target, source, isNonBlocking);
    }

    private StatementAst SkipStatement(XElement e)
    {
        _logger.LogWarning("Skipping unknown statement element '{Name}'.", e.Name.LocalName);
        return new BeginAst([]);
    }

    // ── LValue ──────────────────────────────────────────────────────────────

    private LValueAst ParseLValue(XElement e)
    {
        return e.Name.LocalName switch
        {
            "varref"   => new VarRefLValue(Attr(e, "name")),
            "sel"      => ParseBitSelectLValue(e),
            "arraysel" => ParseArraySelectLValue(e),
            "concat"   => new ConcatLValue(e.Elements().Select(ParseLValue).ToList()),
            _ => FallbackLValue(e)
        };
    }

    private static BitSelectLValue ParseBitSelectLValue(XElement sel)
    {
        string signalName = (string?)sel.Element("varref")?.Attribute("name") ?? "__unknown__";
        List<XElement> consts = sel.Elements("const").ToList();
        if (consts.Count >= 2)
        {
            int lo = ParseVerilogConst(consts[0]);
            int width = ParseVerilogConst(consts[1]);
            return new BitSelectLValue(signalName, new BitRange(lo + Math.Max(1, width) - 1, lo));
        }
        return new BitSelectLValue(signalName, new BitRange(0, 0));
    }

    private ArraySelectLValue ParseArraySelectLValue(XElement e)
    {
        string signalName = (string?)e.Element("varref")?.Attribute("name") ?? "__unknown__";
        XElement? indexEl = e.Elements().Skip(1).FirstOrDefault();
        ExpressionAst index = indexEl is not null ? ParseExpression(indexEl) : new ConstExpr(BigInteger.Zero, 1, false);
        return new ArraySelectLValue(signalName, index);
    }

    private LValueAst FallbackLValue(XElement e)
    {
        _logger.LogWarning("Unknown lvalue element '{Name}', falling back to __unknown__.", e.Name.LocalName);
        return new VarRefLValue("__unknown__");
    }

    // ── Expression ──────────────────────────────────────────────────────────

    private ExpressionAst ParseExpression(XElement e, int depth = 0)
    {
        if (depth > MaxExpressionDepth)
            throw new InvalidDataException(
                $"Expression nesting depth exceeded {MaxExpressionDepth} at element '{e.Name}'. " +
                "The design may contain an unexpanded generate or macro loop.");

        int d = depth + 1;
        return e.Name.LocalName switch
        {
            "varref"    => new SignalRef(Attr(e, "name")),
            "const"     => ParseConst(e),
            "sel"       => ParseSel(e, d),
            "arraysel"  => ParseArraySel(e, d),
            "concat"    => new ConcatExpr(e.Elements().Select(c => ParseExpression(c, d)).ToList()),
            "replicate" => ParseReplicate(e, d),
            "extend"    => ParseExtend(e, d, signed: false),
            "extendS"   => ParseExtend(e, d, signed: true),
            "cond"      => ParseCond(e, d),
            // binary operators
            "add"    => Binary(BinaryOp.Add, e, d),
            "sub"    => Binary(BinaryOp.Sub, e, d),
            "mul"    => Binary(BinaryOp.Mul, e, d),
            "div"    => Binary(BinaryOp.Div, e, d),
            "moddiv" => Binary(BinaryOp.Mod, e, d),
            "and"    => Binary(BinaryOp.And, e, d),
            "or"     => Binary(BinaryOp.Or, e, d),
            "xor"    => Binary(BinaryOp.Xor, e, d),
            "logand" => Binary(BinaryOp.LogicAnd, e, d),
            "logor"  => Binary(BinaryOp.LogicOr, e, d),
            "eq"     => Binary(BinaryOp.Equal, e, d),
            "neq"    => Binary(BinaryOp.NotEqual, e, d),
            "lt"     => Binary(BinaryOp.LessThan, e, d),
            "gt"     => Binary(BinaryOp.GreaterThan, e, d),
            "lte"    => Binary(BinaryOp.LessOrEqual, e, d),
            "gte"    => Binary(BinaryOp.GreaterOrEqual, e, d),
            "shiftl"  => Binary(BinaryOp.ShiftLeft, e, d),
            "shiftr"  => Binary(BinaryOp.ShiftRight, e, d),
            "shiftrs" => Binary(BinaryOp.ShiftRightArithmetic, e, d),
            // unary operators
            "not"    => Unary(UnaryOp.Not, e, d),
            "lognot" => Unary(UnaryOp.LogicNot, e, d),
            "negate" => Unary(UnaryOp.Negate, e, d),
            "redand" => Unary(UnaryOp.ReduceAnd, e, d),
            "redor"  => Unary(UnaryOp.ReduceOr, e, d),
            "redxor" => Unary(UnaryOp.ReduceXor, e, d),
            _ => FallbackExpression(e)
        };
    }

    private static ConstExpr ParseConst(XElement e)
    {
        string? raw = (string?)e.Attribute("name");
        if (string.IsNullOrWhiteSpace(raw))
            return new ConstExpr(BigInteger.Zero, 1, false);

        int apostrophe = raw.IndexOf('\'', StringComparison.Ordinal);
        if (apostrophe < 0)
            return new ConstExpr(BigInteger.TryParse(raw, out BigInteger plain) ? plain : BigInteger.Zero, 32, false);

        int width = ParseInt(raw[..apostrophe], 1);
        string rest = raw[(apostrophe + 1)..];
        if (rest.Length < 2)
            return new ConstExpr(BigInteger.Zero, width, false);

        BigInteger value = char.ToLowerInvariant(rest[0]) switch
        {
            // Prepend "0" so BigInteger treats the value as unsigned (no sign-extension from leading F).
            'h' => BigInteger.TryParse("0" + rest[1..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out BigInteger h) ? h : BigInteger.Zero,
            'd' => BigInteger.TryParse(rest[1..], NumberStyles.Integer, CultureInfo.InvariantCulture, out BigInteger dec) ? dec : BigInteger.Zero,
            'b' => ParseBinaryBigInt(rest[1..]),
            _ => BigInteger.Zero
        };

        return new ConstExpr(value, Math.Max(1, width), false);
    }

    private ExpressionAst ParseSel(XElement e, int depth)
    {
        List<XElement> children = e.Elements().ToList();
        if (children.Count == 0)
            return FallbackExpression(e);

        ExpressionAst baseExpr = ParseExpression(children[0], depth);

        if (children.Count >= 3)
        {
            int lo = ParseVerilogConst(children[1]);
            int width = ParseVerilogConst(children[2]);
            return new BitSelectExpr(baseExpr, new BitRange(lo + Math.Max(1, width) - 1, lo));
        }

        // Fallback: no range info, treat as transparent reference
        return baseExpr;
    }

    private ExpressionAst ParseArraySel(XElement e, int depth)
    {
        List<XElement> children = e.Elements().ToList();
        ExpressionAst baseExpr = children.Count > 0 ? ParseExpression(children[0], depth) : FallbackExpression(e);
        ExpressionAst index = children.Count > 1 ? ParseExpression(children[1], depth) : new ConstExpr(BigInteger.Zero, 1, false);
        return new ArraySelectExpr(baseExpr, index);
    }

    private ExpressionAst ParseReplicate(XElement e, int depth)
    {
        List<XElement> children = e.Elements().ToList();
        int count = children.Count > 0 ? (int)ParseConst(children[0]).Value : 1;
        ExpressionAst pattern = children.Count > 1 ? ParseExpression(children[1], depth) : FallbackExpression(e);
        return new ReplicateExpr(count, pattern);
    }

    private ExpressionAst ParseExtend(XElement e, int depth, bool signed)
    {
        XElement? inner = e.Elements().FirstOrDefault();
        ExpressionAst innerExpr = inner is not null ? ParseExpression(inner, depth) : FallbackExpression(e);
        // Target width from dtype_id would require a dtype map reference; use 0 as sentinel for now.
        // The flattener treats ExtendExpr as a wire alias, so exact width is not critical for Phase 1.
        return new ExtendExpr(innerExpr, 0, signed);
    }

    private ExpressionAst ParseCond(XElement e, int depth)
    {
        List<XElement> children = e.Elements().ToList();
        ExpressionAst cond   = children.Count > 0 ? ParseExpression(children[0], depth) : FallbackExpression(e);
        ExpressionAst ifTrue = children.Count > 1 ? ParseExpression(children[1], depth) : new ConstExpr(BigInteger.Zero, 1, false);
        ExpressionAst ifFalse = children.Count > 2 ? ParseExpression(children[2], depth) : new ConstExpr(BigInteger.Zero, 1, false);
        return new CondExpr(cond, ifTrue, ifFalse);
    }

    private BinaryExpr Binary(BinaryOp op, XElement e, int depth)
    {
        List<XElement> children = e.Elements().ToList();
        ExpressionAst left  = children.Count > 0 ? ParseExpression(children[0], depth) : new ConstExpr(BigInteger.Zero, 1, false);
        ExpressionAst right = children.Count > 1 ? ParseExpression(children[1], depth) : new ConstExpr(BigInteger.Zero, 1, false);
        return new BinaryExpr(op, left, right);
    }

    private UnaryExpr Unary(UnaryOp op, XElement e, int depth)
    {
        XElement? child = e.Elements().FirstOrDefault();
        ExpressionAst operand = child is not null ? ParseExpression(child, depth) : new ConstExpr(BigInteger.Zero, 1, false);
        return new UnaryExpr(op, operand);
    }

    private ExpressionAst FallbackExpression(XElement e)
    {
        _logger.LogWarning("Unknown expression element '{Name}', substituting zero constant.", e.Name.LocalName);
        return new ConstExpr(BigInteger.Zero, 1, false);
    }

    // ── IsRegistered post-parse pass ────────────────────────────────────────

    private static List<ModuleAst> ComputeIsRegistered(List<ModuleAst> modules)
    {
        return modules.Select(ComputeModuleIsRegistered).ToList();
    }

    private static ModuleAst ComputeModuleIsRegistered(ModuleAst module)
    {
        if (module.SequentialBlocks.Count == 0)
            return module;

        HashSet<string> driven = CollectSequentialTargets(module.SequentialBlocks);
        List<SignalDecl> updated = module.LocalSignals
            .Select(s => driven.Contains(s.Name) ? s with { IsRegistered = true } : s)
            .ToList();
        return module with { LocalSignals = updated };
    }

    private static HashSet<string> CollectSequentialTargets(IReadOnlyList<SequentialBlockAst> blocks)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (SequentialBlockAst block in blocks)
            CollectFromStatement(block.Body, names);
        return names;
    }

    private static void CollectFromStatement(StatementAst stmt, HashSet<string> names)
    {
        switch (stmt)
        {
            case BeginAst begin:
                foreach (StatementAst s in begin.Statements)
                    CollectFromStatement(s, names);
                break;
            case IfAst ifAst:
                CollectFromStatement(ifAst.Then, names);
                if (ifAst.Else is not null) CollectFromStatement(ifAst.Else, names);
                break;
            case CaseAst caseAst:
                foreach (CaseArm arm in caseAst.Arms) CollectFromStatement(arm.Body, names);
                if (caseAst.Default is not null) CollectFromStatement(caseAst.Default, names);
                break;
            case AssignAst assign:
                CollectLValueNames(assign.Target, names);
                break;
        }
    }

    private static void CollectLValueNames(LValueAst lval, HashSet<string> names)
    {
        switch (lval)
        {
            case VarRefLValue v:       names.Add(v.Name); break;
            case BitSelectLValue b:    names.Add(b.SignalName); break;
            case ArraySelectLValue a:  names.Add(a.SignalName); break;
            case StructFieldLValue sf: names.Add(sf.SignalName); break;
            case ConcatLValue c:
                foreach (LValueAst part in c.Parts) CollectLValueNames(part, names);
                break;
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static DType LookupDType(Dictionary<string, DType> dtypes, string? id)
        => id is not null && dtypes.TryGetValue(id, out DType? dt) ? dt : DType.Scalar;

    private static string Attr(XElement e, string name)
        => (string?)e.Attribute(name) ?? string.Empty;

    private static int ParseInt(string? value, int fallback)
        => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : fallback;

    private static int ParseVerilogConst(XElement e)
    {
        string? raw = (string?)e.Attribute("name");
        if (string.IsNullOrWhiteSpace(raw)) return 0;
        int apos = raw.IndexOf('\'', StringComparison.Ordinal);
        if (apos < 0) return ParseInt(raw, 0);
        string rest = raw[(apos + 1)..];
        if (rest.Length < 2) return 0;
        return char.ToLowerInvariant(rest[0]) switch
        {
            'h' => int.TryParse(rest[1..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int h) ? h : 0,
            'd' => ParseInt(rest[1..], 0),
            'b' => (int)ParseBinaryBigInt(rest[1..]),
            _ => 0
        };
    }

    private static BigInteger ParseBinaryBigInt(string s)
    {
        BigInteger result = BigInteger.Zero;
        foreach (char c in s.Where(static c => c is '0' or '1'))
            result = (result << 1) | (c - '0');
        return result;
    }

    private static SignalDirection ParseDirection(string value) => value switch
    {
        "input"  => SignalDirection.Input,
        "output" => SignalDirection.Output,
        "inout"  => SignalDirection.InOut,
        _        => SignalDirection.Internal
    };

    private sealed record DType(string? Id, int Width, bool IsSigned, IReadOnlyList<BitRange> ArrayDims)
    {
        public static DType Scalar { get; } = new(null, 1, false, []);
    }
}
