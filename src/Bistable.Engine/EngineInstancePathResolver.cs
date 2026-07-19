using Bistable.Core.Design.Ast;

namespace Bistable.Engine;

/// <summary>
/// A hierarchical instance path that does not resolve to a module in the
/// elaborated design. Carries a structured, frontend-presentable message.
/// </summary>
public sealed class InvalidInstancePathException(string message) : Exception(message);

/// <summary>
/// Resolves a hierarchical instance path (<c>top.u_core.u_alu</c>) to the
/// module definition it instantiates. The instance path — never the module
/// type name — is the identity of a hierarchical schematic document, so two
/// instances of the same module type resolve independently and keep distinct
/// probe prefixes.
/// </summary>
public static class EngineInstancePathResolver
{
    public static ModuleAst Resolve(DesignAst ast, string instancePath)
    {
        ArgumentNullException.ThrowIfNull(ast);
        if (string.IsNullOrWhiteSpace(instancePath))
        {
            throw new InvalidInstancePathException("Instance path must not be empty.");
        }

        ModuleAst top = ast.TopModule
            ?? throw new InvalidInstancePathException("Elaborated design has no top module.");
        string[] segments = instancePath.Split('.');
        if (!SegmentMatchesModule(segments[0], top))
        {
            throw new InvalidInstancePathException(
                $"Instance path '{instancePath}' must start at the top module '{top.SourceName}'.");
        }

        ModuleAst current = top;
        for (int index = 1; index < segments.Length; index++)
        {
            string segment = segments[index];
            InstanceDecl? instance = current.Instances
                .FirstOrDefault(candidate => string.Equals(candidate.InstanceName, segment, StringComparison.Ordinal));
            if (instance is null)
            {
                throw new InvalidInstancePathException(
                    $"Module '{current.SourceName}' has no instance named '{segment}' "
                    + $"(while resolving '{instancePath}').");
            }
            current = FindModule(ast, instance.ModuleName)
                ?? throw new InvalidInstancePathException(
                    $"Instance '{segment}' refers to module '{instance.ModuleName}', "
                    + $"which is not part of the elaborated design (while resolving '{instancePath}').");
        }
        return current;
    }

    private static bool SegmentMatchesModule(string segment, ModuleAst module) =>
        string.Equals(segment, module.Name, StringComparison.Ordinal)
        || string.Equals(segment, module.SourceName, StringComparison.Ordinal);

    private static ModuleAst? FindModule(DesignAst ast, string moduleName) =>
        ast.Modules.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, moduleName, StringComparison.Ordinal))
        ?? ast.Modules.FirstOrDefault(candidate =>
            string.Equals(candidate.SourceName, moduleName, StringComparison.Ordinal));
}
