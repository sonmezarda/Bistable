using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Bistable.Core.Design.Ast.Passes;

/// <summary>Content-addressed module diff used by Phase 9 live elaboration.</summary>
public static class AstModuleDiff
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false
    };

    public static AstModuleDiffResult Compare(DesignAst? previous, DesignAst current, string topModule)
    {
        ArgumentNullException.ThrowIfNull(current);
        Dictionary<string, string> oldHashes = HashModules(previous);
        Dictionary<string, string> newHashes = HashModules(current);
        HashSet<string> added = new(newHashes.Keys.Except(oldHashes.Keys, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
        HashSet<string> removed = new(oldHashes.Keys.Except(newHashes.Keys, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
        HashSet<string> changed = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string name, string hash) in newHashes)
        {
            if (oldHashes.TryGetValue(name, out string? prior) && !string.Equals(prior, hash, StringComparison.Ordinal))
            {
                changed.Add(name);
            }
        }

        ModuleAst? oldTop = previous?.Modules.FirstOrDefault(module =>
            string.Equals(module.Name, topModule, StringComparison.OrdinalIgnoreCase));
        ModuleAst? newTop = current.Modules.FirstOrDefault(module =>
            string.Equals(module.Name, topModule, StringComparison.OrdinalIgnoreCase));
        bool interfaceChanged = oldTop is null
            || newTop is null
            || !string.Equals(ComputeInterfaceHash(oldTop), ComputeInterfaceHash(newTop), StringComparison.Ordinal);
        return new AstModuleDiffResult(added, removed, changed, interfaceChanged, oldHashes, newHashes);
    }

    public static string ComputeContentHash(ModuleAst module) => Hash(JsonSerializer.Serialize(module, JsonOptions));

    public static string ComputeInterfaceHash(ModuleAst module)
    {
        StringBuilder canonical = new();
        foreach (PortDecl port in module.Ports.OrderBy(static port => port.PinIndex))
        {
            canonical.Append(port.PinIndex).Append(':')
                .Append(port.Name).Append(':')
                .Append(port.Direction).Append(':')
                .Append(port.Width).Append(':')
                .Append(port.IsSigned ? 's' : 'u').Append('|');
        }
        return Hash(canonical.ToString());
    }

    private static Dictionary<string, string> HashModules(DesignAst? ast) =>
        ast?.Modules.ToDictionary(
            static module => module.Name,
            ComputeContentHash,
            StringComparer.OrdinalIgnoreCase)
        ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

public sealed record AstModuleDiffResult(
    IReadOnlySet<string> AddedModules,
    IReadOnlySet<string> RemovedModules,
    IReadOnlySet<string> ChangedModules,
    bool TopInterfaceChanged,
    IReadOnlyDictionary<string, string> PreviousHashes,
    IReadOnlyDictionary<string, string> CurrentHashes)
{
    public IReadOnlySet<string> DirtyModules { get; } = new HashSet<string>(
        AddedModules.Concat(RemovedModules).Concat(ChangedModules),
        StringComparer.OrdinalIgnoreCase);

    public bool HasChanges => DirtyModules.Count > 0;
}
