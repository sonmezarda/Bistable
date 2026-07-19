using Bistable.Core.Design.Ast;
using Bistable.Core.Design.Schematic;

namespace Bistable.Engine;

/// <summary>
/// Vivado-style selective inline expansion: composes one module's schematic
/// graph with chosen child instances expanded in place as nested Container
/// nodes. Expanded internals keep exact per-bit net identity — every child
/// signal is namespaced with its instance name (<c>u_alu.zero</c>), so the
/// document prefix still yields the true hierarchical probe path. Boundary
/// ports of an expanded instance become pass-through Port symbols connecting
/// the parent net to the namespaced internal net; nothing is aliased.
/// </summary>
public sealed class EngineSchematicComposer
{
    /// <summary>
    /// Composes the schematic of <paramref name="instancePath"/> with the given
    /// relative instance paths (e.g. <c>u_core</c>, <c>u_core.u_alu</c>)
    /// expanded inline. An empty set degenerates to the plain projection.
    /// </summary>
    public EngineSchematicGraph Compose(
        DesignAst ast,
        string instancePath,
        IReadOnlyCollection<string> expandPaths)
    {
        ModuleAst module = EngineInstancePathResolver.Resolve(ast, instancePath);
        ExpansionNode tree = ExpansionNode.Build(expandPaths);
        List<EngineSchematicNode> nodes = ComposeNodes(ast, module, tree, instancePath);
        return EngineSchematicProjectionService.FinishGraph(module.SourceName, nodes);
    }

    private static List<EngineSchematicNode> ComposeNodes(
        DesignAst ast,
        ModuleAst module,
        ExpansionNode tree,
        string contextPath)
    {
        SchematicPrimitiveList primitives = SchematicDecoder.Decode(module);
        List<EngineSchematicNode> nodes = EngineSchematicProjectionService.ProjectNodes(primitives);
        foreach ((string instanceName, ExpansionNode subtree) in tree.Children)
        {
            InstancePrimitive instance = primitives.Instances
                .FirstOrDefault(candidate => string.Equals(candidate.InstanceName, instanceName, StringComparison.Ordinal))
                ?? throw new InvalidInstancePathException(
                    $"Module '{module.SourceName}' has no instance named '{instanceName}' "
                    + $"(while expanding under '{contextPath}').");
            ModuleAst childModule = FindModule(ast, instance.ModuleName)
                ?? throw new InvalidInstancePathException(
                    $"Instance '{instanceName}' refers to module '{instance.ModuleName}', "
                    + $"which is not part of the elaborated design (while expanding under '{contextPath}').");

            List<EngineSchematicNode> childNodes = ComposeNodes(
                ast, childModule, subtree, $"{contextPath}.{instanceName}");

            // The collapsed Instance symbol is replaced by a Container that ELK
            // sizes around the expanded internals.
            string containerId = $"container:{instanceName}";
            nodes.RemoveAll(node => string.Equals(node.Id, instance.Id, StringComparison.Ordinal));
            nodes.Add(new EngineSchematicNode(
                containerId, "Container", instance.InstanceName, [], [],
                TypeLabel: childModule.SourceName));

            Dictionary<string, InstancePinBinding> bindings = instance.Pins
                .Where(static pin => !string.IsNullOrWhiteSpace(pin.SignalName) && pin.SignalName != "?")
                .GroupBy(static pin => pin.PortName, StringComparer.Ordinal)
                .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.Ordinal);
            nodes.AddRange(childNodes.Select(child =>
                NamespaceChild(child, instanceName, containerId, bindings)));
        }
        return nodes;
    }

    /// <summary>
    /// Moves one composed child node into the parent scope: ids are prefixed
    /// with <c>{instance}/</c>, signals with <c>{instance}.</c>, and the direct
    /// boundary Port nodes are rewired as pass-throughs between the parent net
    /// and the namespaced internal net.
    /// </summary>
    private static EngineSchematicNode NamespaceChild(
        EngineSchematicNode node,
        string instanceName,
        string containerId,
        IReadOnlyDictionary<string, InstancePinBinding> bindings)
    {
        string Ns(string signal) => $"{instanceName}.{signal}";
        string id = $"{instanceName}/{node.Id}";
        string nestedContainerId = node.ContainerId is null ? containerId : $"{instanceName}/{node.ContainerId}";

        // Only the child module's own boundary ports (root-level Port nodes)
        // are rewired; ports of deeper expansions were already rewired one
        // level down and are plain-namespaced here.
        if (node.Kind == "Port" && node.ContainerId is null
            && bindings.TryGetValue(node.Label, out InstancePinBinding? binding))
        {
            bool isInputPort = node.Outputs.Count > 0 && node.Inputs.Count == 0;
            bool isOutputPort = node.Inputs.Count > 0 && node.Outputs.Count == 0;
            if (isInputPort)
            {
                return node with
                {
                    Id = id,
                    ContainerId = nestedContainerId,
                    Inputs = [binding.SignalName],
                    Outputs = [.. node.Outputs.Select(Ns)],
                    // Direction hint for the renderer: a pass-through port has
                    // both sides, so shape can no longer be inferred from them.
                    TypeLabel = "input"
                };
            }
            if (isOutputPort)
            {
                return node with
                {
                    Id = id,
                    ContainerId = nestedContainerId,
                    Inputs = [.. node.Inputs.Select(Ns)],
                    Outputs = [binding.SignalName],
                    TypeLabel = "output"
                };
            }
            // InOut boundary ports keep plain namespacing for now.
        }

        return node with
        {
            Id = id,
            ContainerId = nestedContainerId,
            Inputs = [.. node.Inputs.Select(Ns)],
            Outputs = [.. node.Outputs.Select(Ns)]
        };
    }

    private static ModuleAst? FindModule(DesignAst ast, string moduleName) =>
        ast.Modules.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, moduleName, StringComparison.Ordinal))
        ?? ast.Modules.FirstOrDefault(candidate =>
            string.Equals(candidate.SourceName, moduleName, StringComparison.Ordinal));

    /// <summary>Relative expansion paths as a trie of instance names.</summary>
    private sealed class ExpansionNode
    {
        public Dictionary<string, ExpansionNode> Children { get; } = new(StringComparer.Ordinal);

        public static ExpansionNode Build(IReadOnlyCollection<string> expandPaths)
        {
            ExpansionNode root = new();
            foreach (string path in expandPaths)
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    throw new InvalidInstancePathException("Expansion paths must not be empty.");
                }
                ExpansionNode current = root;
                foreach (string segment in path.Split('.'))
                {
                    if (string.IsNullOrWhiteSpace(segment))
                    {
                        throw new InvalidInstancePathException(
                            $"Expansion path '{path}' contains an empty segment.");
                    }
                    if (!current.Children.TryGetValue(segment, out ExpansionNode? child))
                    {
                        child = new ExpansionNode();
                        current.Children.Add(segment, child);
                    }
                    current = child;
                }
            }
            return root;
        }
    }
}
