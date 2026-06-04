using Bistable.Core.Design.Ast;

namespace Bistable.Verilator;

/// <summary>
/// Walks a <see cref="DesignAst"/> hierarchy and yields one descriptor per
/// probable hierarchical signal. Used by the worker code-generator to populate
/// the C++ probe table that backs the GUI's hot-read/write API.
/// </summary>
public static class ProbeTableEnumerator
{
    /// <summary>
    /// Maximum signal width that fits in a uint64_t (Verilator CData/SData/IData/QData).
    /// Wider signals (VlWide&lt;N&gt;) require special hex-string handling — emitted as
    /// probes only in a later iteration.
    /// </summary>
    public const int MaxScalarWidth = 64;

    /// <summary>
    /// Enumerates every probable signal in the design, starting from the top
    /// module. Each entry carries the dotted hierarchy path (the key the GUI
    /// uses), the underlying signal/port info, and a memory flag.
    /// </summary>
    public static IEnumerable<ProbeEntry> Enumerate(DesignAst ast, string topModuleName)
    {
        ArgumentNullException.ThrowIfNull(ast);
        ArgumentException.ThrowIfNullOrWhiteSpace(topModuleName);

        ModuleAst? topModule = ast.Modules
            .FirstOrDefault(m => string.Equals(m.Name, topModuleName, StringComparison.OrdinalIgnoreCase));
        if (topModule is null) yield break;

        Dictionary<string, ModuleAst> catalog = ast.Modules
            .GroupBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        foreach (ProbeEntry entry in EnumerateModule(topModule, topModuleName, catalog, depth: 0))
            yield return entry;
    }

    private static IEnumerable<ProbeEntry> EnumerateModule(
        ModuleAst module,
        string pathPrefix,
        IReadOnlyDictionary<string, ModuleAst> catalog,
        int depth)
    {
        // Defensive bound: pathological designs with deep instance chains
        // shouldn't blow the stack. 200 is well past any real CPU's depth.
        if (depth > 200) yield break;

        // Top-level ports — emitted before locals so callers can pick them up
        // before any internal-only signals.
        foreach (PortDecl port in module.Ports)
        {
            string path = pathPrefix + "." + port.Name;
            yield return new ProbeEntry(
                Path: path,
                FieldName: MangleFieldName(path),
                Width: port.Width,
                IsSigned: port.IsSigned,
                IsRegistered: false,
                IsMemory: false,
                MemoryDepth: null);
        }

        // Local signals — scalar OR memory (P3-6). Wide buses and Verilator
        // internal tmps still filtered.
        foreach (SignalDecl signal in module.LocalSignals)
        {
            ProbeEntry? entry = TryBuildLocalSignalProbe(signal, pathPrefix);
            if (entry is not null) yield return entry;
        }

        // Recurse into sub-instances
        foreach (InstanceDecl instance in module.Instances)
        {
            if (!catalog.TryGetValue(instance.ModuleName, out ModuleAst? subModule)) continue;
            string subPrefix = pathPrefix + "." + instance.InstanceName;
            foreach (ProbeEntry inner in EnumerateModule(subModule, subPrefix, catalog, depth + 1))
                yield return inner;
        }
    }

    private static ProbeEntry? TryBuildLocalSignalProbe(SignalDecl signal, string pathPrefix)
    {
        if (IsVerilatorInternalSignal(signal.Name)) return null;
        if (signal.Width > MaxScalarWidth) return null;   // TODO: wide-signal hex path

        string path = pathPrefix + "." + signal.Name;
        // Yosys-flattened netlists produce escaped wires (`\u_alu.carry`)
        // whose AST Name contains a "." that is part of the identifier, not a
        // hierarchy separator. Verilator mangles those characters ("." →
        // "__02e") and exposes the result as `origName`. When present that's
        // the only correct way to spell the C++ field — splitting Name on "."
        // would point at a member that doesn't exist.
        string fieldName = signal.OrigName is { Length: > 0 } orig
            ? pathPrefix.Replace(".", "__DOT__", StringComparison.Ordinal) + "__DOT__" + orig
            : MangleFieldName(path);

        if (signal.ArrayDims.Count == 0)
        {
            return new ProbeEntry(
                Path: path,
                FieldName: fieldName,
                Width: signal.Width,
                IsSigned: signal.IsSigned,
                IsRegistered: signal.IsRegistered,
                IsMemory: false,
                MemoryDepth: null);
        }

        // P3-6: a single unpacked dimension is the common case (registers/RAM).
        // Multi-dim arrays still not handled.
        if (signal.ArrayDims.Count != 1) return null;
        BitRange dim = signal.ArrayDims[0];
        return new ProbeEntry(
            Path: path,
            FieldName: fieldName,
            Width: signal.Width,
            IsSigned: signal.IsSigned,
            IsRegistered: false,
            IsMemory: true,
            MemoryDepth: dim.Width);
    }

    /// <summary>
    /// Converts a dotted hierarchy path (e.g. <c>"arnicomp_top.acc.reg_q"</c>) to
    /// Verilator's flat field name (<c>"arnicomp_top__DOT__acc__DOT__reg_q"</c>).
    /// The C++ probe table addresses fields as <c>model-&gt;rootp-&gt;{FieldName}</c>.
    /// </summary>
    public static string MangleFieldName(string hierarchyPath)
        => hierarchyPath.Replace(".", "__DOT__", StringComparison.Ordinal);

    /// <summary>
    /// Returns true for Verilator-generated internal helper signals (the same
    /// <c>__V</c> prefix discriminator used by the schematic decoder). These
    /// are CSE/DFG temporaries with garbage names — never user-meaningful.
    /// </summary>
    public static bool IsVerilatorInternalSignal(string name) =>
        !string.IsNullOrEmpty(name) && name.StartsWith("__V", StringComparison.Ordinal);
}

/// <summary>One entry in the worker's probe table — produced by enumeration, consumed by code-gen.</summary>
/// <param name="Path">Dotted hierarchy path (the GUI's lookup key).</param>
/// <param name="FieldName">Verilator's mangled flat field name (<c>__DOT__</c> separators).</param>
/// <param name="Width">Bit width of the signal.</param>
/// <param name="IsSigned">Declared signed.</param>
/// <param name="IsRegistered">True for FF Q values (target of a sequential block).</param>
/// <param name="IsMemory">True for unpacked arrays.</param>
/// <param name="MemoryDepth">Number of memory cells for memory probes; null for scalar.</param>
public sealed record ProbeEntry(
    string Path,
    string FieldName,
    int Width,
    bool IsSigned,
    bool IsRegistered,
    bool IsMemory,
    int? MemoryDepth);
