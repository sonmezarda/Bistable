using Bistable.App.Services.Routing.Elk;
using Bistable.App.ViewModels;
using Bistable.Core.Design;
using Bistable.Core.Design.Ast;
using Bistable.Core.Design.Schematic;
using Bistable.Verilator;

namespace Bistable.Regression;

public sealed class AlwaysCombSchematicIntegrityRegressionTests
{
    [Fact]
    public void RiscvSingleCycle_AluZero_HasConsumerEdgeIntoProjectedBranchMux()
    {
        string xmlPath = ResolveRepoFile(
            "samples/riscv_single_cycle/.bistable/metadata/riscv_single_cycle_top.xml");
        DesignAst design = new VerilatorXmlAstReader().Read(xmlPath);
        ModuleAst top = design.TopModule!;
        ElaboratedDesign flattened = LegacyDesignFlattener.Flatten(design, top.Name);
        SchematicPrimitiveList primitives = SchematicDecoder.Decode(top);

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData(
                BoundaryPorts: top.Ports
                    .OrderBy(static port => port.PinIndex)
                    .Select(static port => new HierarchyScopePortViewModel(
                        port.Name, port.Direction, port.Width, port.IsSigned))
                    .ToList(),
                ChildScopes: BuildChildScopes(design, top),
                LocalSignals: top.LocalSignals
                    .Select(static signal => new HierarchyScopeLocalSignalViewModel(
                        signal.Name,
                        signal.Width,
                        signal.IsSigned,
                        isTraced: false,
                        currentValue: "",
                        resolvedSignalName: null))
                    .ToList(),
                ContAssigns: flattened.ModuleDefinitions[top.Name].ContAssigns,
                Primitives: primitives.Logic),
            compactLayout: true);

        IReadOnlyList<ElkPortRef> producers = result.RoutingTelemetry.ProducersBySignal["alu_zero"];
        IReadOnlyList<ElkPortRef> consumers = result.RoutingTelemetry.ConsumersBySignal["alu_zero"];
        HashSet<string> producerPorts = producers.Select(static port => port.PortId).ToHashSet(StringComparer.Ordinal);
        HashSet<string> consumerPorts = consumers.Select(static port => port.PortId).ToHashSet(StringComparer.Ordinal);

        ElkEdge[] edges =
        [
            .. result.Graph.Edges.Where(candidate =>
                candidate.Sources.Any(producerPorts.Contains)
                && candidate.Targets.Any(consumerPorts.Contains))
        ];
        Assert.NotEmpty(edges);
        Assert.Contains(edges,
            static edge => edge.Sources.Any(source => source.Contains("u_alu", StringComparison.Ordinal))
                           && edge.Targets.Any(target => target.StartsWith("mux_branch_taken", StringComparison.Ordinal)));
    }

    private static IReadOnlyList<HierarchyScopeInstanceViewModel> BuildChildScopes(
        DesignAst design,
        ModuleAst top)
    {
        Dictionary<string, ModuleAst> modules = design.Modules
            .ToDictionary(static module => module.Name, StringComparer.OrdinalIgnoreCase);

        return top.Instances.Select(instance =>
        {
            modules.TryGetValue(instance.ModuleName, out ModuleAst? childModule);
            Dictionary<string, int> widths = childModule?.Ports
                .ToDictionary(static port => port.Name, static port => port.Width, StringComparer.OrdinalIgnoreCase)
                ?? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            List<HierarchyScopeInstancePortConnectionViewModel> connections = instance.PortConnections
                .OrderBy(static connection => connection.PortIndex)
                .Select(connection => new HierarchyScopeInstancePortConnectionViewModel(
                    connection.PortName,
                    connection.SignalName,
                    string.Equals(connection.Direction, "in", StringComparison.OrdinalIgnoreCase),
                    widths.GetValueOrDefault(connection.PortName, 1),
                    connection.ConcatParts))
                .ToList();

            return new HierarchyScopeInstanceViewModel(
                $"{top.Name}.{instance.InstanceName}",
                instance.InstanceName,
                instance.ModuleName,
                connections.Count(static connection => connection.IsInput),
                connections.Count(static connection => connection.IsOutput),
                exactSignalCount: 0,
                descendantSignalCount: 0,
                connections);
        }).ToList();
    }

    private static string ResolveRepoFile(string relativePath)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate repo file '{relativePath}'.");
    }
}
