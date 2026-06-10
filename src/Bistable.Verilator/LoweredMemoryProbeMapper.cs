using Bistable.Core.Design.Ast;
using System.Text.Json.Serialization;

namespace Bistable.Verilator;

/// <summary>
/// Reconstructs logical RTL memories after synthesis has lowered an unpacked
/// array into independently-addressable scalar fields. Memory identity comes
/// exclusively from the source AST; synthesized names are only used to locate
/// the physical fields that implement each declared element.
/// </summary>
public static class LoweredMemoryProbeMapper
{
    public static LoweredMemoryProbeMap Build(
        DesignAst sourceAst,
        DesignAst synthesizedAst,
        string topModuleName)
    {
        ArgumentNullException.ThrowIfNull(sourceAst);
        ArgumentNullException.ThrowIfNull(synthesizedAst);
        ArgumentException.ThrowIfNullOrWhiteSpace(topModuleName);

        IReadOnlyList<ProbeEntry> synthesizedProbes =
            ProbeTableEnumerator.Enumerate(synthesizedAst, topModuleName).ToList();
        Dictionary<string, ProbeEntry> probesByPath = synthesizedProbes
            .GroupBy(static probe => probe.Path, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.Ordinal);

        List<ProbeEntry> supplementalProbes = [];
        List<GateMemoryProbeMapping> mappings = [];
        foreach (LogicalMemory memory in EnumerateLogicalMemories(sourceAst, topModuleName))
        {
            if (memory.UnsupportedReason is not null)
            {
                mappings.Add(new GateMemoryProbeMapping(
                    memory.Path,
                    memory.CellWidth,
                    memory.Depth,
                    GateMemoryMappingKind.Unresolved,
                    [],
                    memory.UnsupportedReason));
                continue;
            }

            if (probesByPath.TryGetValue(memory.Path, out ProbeEntry? nativeMemory)
                && nativeMemory.IsMemory)
            {
                mappings.Add(new GateMemoryProbeMapping(
                    memory.Path,
                    memory.CellWidth,
                    memory.Depth,
                    GateMemoryMappingKind.NativeArray,
                    [memory.Path]));
                continue;
            }

            List<ProbeEntry> elements = new(memory.Depth);
            for (int address = 0; address < memory.Depth; address++)
            {
                string elementPath = $"{memory.Path}[{address}]";
                if (probesByPath.TryGetValue(elementPath, out ProbeEntry? element)
                    && !element.IsMemory
                    && element.Width == memory.CellWidth)
                {
                    elements.Add(element);
                }
            }

            if (elements.Count != memory.Depth)
            {
                mappings.Add(new GateMemoryProbeMapping(
                    memory.Path,
                    memory.CellWidth,
                    memory.Depth,
                    GateMemoryMappingKind.Unresolved,
                    elements.Select(static element => element.Path).ToArray(),
                    $"Resolved {elements.Count} of {memory.Depth} synthesized elements."));
                continue;
            }

            supplementalProbes.Add(new ProbeEntry(
                Path: memory.Path,
                FieldName: string.Empty,
                Width: memory.CellWidth,
                IsSigned: memory.IsSigned,
                IsRegistered: false,
                IsMemory: true,
                MemoryDepth: memory.Depth,
                MemoryElementFieldNames: elements.Select(static element => element.FieldName).ToArray()));
            mappings.Add(new GateMemoryProbeMapping(
                memory.Path,
                memory.CellWidth,
                memory.Depth,
                GateMemoryMappingKind.LoweredElements,
                elements.Select(static element => element.Path).ToArray()));
        }

        return new LoweredMemoryProbeMap(
            supplementalProbes,
            new GateRuntimeProbeManifest(topModuleName, mappings));
    }

    private static IEnumerable<LogicalMemory> EnumerateLogicalMemories(
        DesignAst sourceAst,
        string topModuleName)
    {
        Dictionary<string, ModuleAst> modules = sourceAst.Modules
            .GroupBy(static module => module.Name, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.Ordinal);
        if (!modules.TryGetValue(topModuleName, out ModuleAst? top))
        {
            yield break;
        }

        foreach (LogicalMemory memory in EnumerateModule(top, topModuleName, modules, depth: 0))
        {
            yield return memory;
        }
    }

    private static IEnumerable<LogicalMemory> EnumerateModule(
        ModuleAst module,
        string hierarchyPath,
        IReadOnlyDictionary<string, ModuleAst> modules,
        int depth)
    {
        if (depth > 200)
        {
            yield break;
        }

        foreach (SignalDecl signal in module.LocalSignals)
        {
            if (signal.ArrayDims.Count == 0)
            {
                continue;
            }

            string path = $"{hierarchyPath}.{signal.Name}";
            if (signal.ArrayDims.Count != 1)
            {
                yield return new LogicalMemory(
                    path,
                    signal.Width,
                    Depth: 0,
                    signal.IsSigned,
                    "Multi-dimensional memories are not supported by the worker memory protocol.");
                continue;
            }

            BitRange dimension = signal.ArrayDims[0];
            string? unsupportedReason = signal.Width > ProbeTableEnumerator.MaxScalarWidth
                ? $"Cell width {signal.Width} exceeds the worker's {ProbeTableEnumerator.MaxScalarWidth}-bit scalar limit."
                : null;
            yield return new LogicalMemory(
                path,
                signal.Width,
                dimension.Width,
                signal.IsSigned,
                unsupportedReason);
        }

        foreach (InstanceDecl instance in module.Instances)
        {
            if (!modules.TryGetValue(instance.ModuleName, out ModuleAst? child))
            {
                continue;
            }

            foreach (LogicalMemory memory in EnumerateModule(
                         child,
                         $"{hierarchyPath}.{instance.InstanceName}",
                         modules,
                         depth + 1))
            {
                yield return memory;
            }
        }
    }

    private sealed record LogicalMemory(
        string Path,
        int CellWidth,
        int Depth,
        bool IsSigned,
        string? UnsupportedReason = null);
}

public sealed record LoweredMemoryProbeMap(
    IReadOnlyList<ProbeEntry> SupplementalProbes,
    GateRuntimeProbeManifest Manifest);

public sealed record GateRuntimeProbeManifest(
    string TopModule,
    IReadOnlyList<GateMemoryProbeMapping> Memories)
{
    [JsonIgnore]
    public IReadOnlyList<GateMemoryProbeMapping> UnresolvedMemories =>
        Memories.Where(static memory => memory.Kind == GateMemoryMappingKind.Unresolved).ToArray();
}

public sealed record GateMemoryProbeMapping(
    string LogicalPath,
    int CellWidth,
    int Depth,
    GateMemoryMappingKind Kind,
    IReadOnlyList<string> PhysicalProbePaths,
    string? Diagnostic = null);

public enum GateMemoryMappingKind
{
    NativeArray,
    LoweredElements,
    Unresolved,
}
