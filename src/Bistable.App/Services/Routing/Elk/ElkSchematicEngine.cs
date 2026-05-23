using System.Security.Cryptography;
using System.Text;
using Bistable.App.ViewModels;

namespace Bistable.App.Services.Routing.Elk;

/// <summary>
/// Orchestrates the ELK pipeline:
///   scope view-models → ElkGraphBuilder → ElkRunner (Node subprocess) → cached layout.
/// </summary>
public sealed class ElkSchematicEngine
{
    private readonly ElkGraphBuilder _builder = new();
    private readonly ElkRunner _runner;
    private string? _cacheKey;
    private ElkLayoutResult? _cachedResult;
    private string? _cachedError;

    public ElkSchematicEngine()
        : this(new ElkRunner())
    {
    }

    public ElkSchematicEngine(ElkRunner runner)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
    }

    public ElkLayoutResult Compute(ElkScopeData scope, bool compactLayout)
    {
        string key = ComputeCacheKey(scope, compactLayout);
        if (string.Equals(_cacheKey, key, StringComparison.Ordinal))
        {
            if (_cachedResult is not null)
            {
                return _cachedResult;
            }

            if (_cachedError is not null)
            {
                throw new SchematicRoutingException(_cachedError);
            }
        }

        try
        {
            ElkBuildResult build = _builder.Build(scope, compactLayout);
            ElkGraph layouted = _runner.Layout(build.Graph);
            ElkLayoutResult result = new(layouted, build.PortRefs);
            _cacheKey = key;
            _cachedResult = result;
            _cachedError = null;
            return result;
        }
        catch (SchematicRoutingException ex)
        {
            _cacheKey = key;
            _cachedResult = null;
            _cachedError = ex.Message;
            throw;
        }
    }

    private static string ComputeCacheKey(ElkScopeData scope, bool compactLayout)
    {
        StringBuilder sb = new();
        sb.Append(compactLayout ? "C|" : "N|");
        foreach (HierarchyScopePortViewModel port in scope.BoundaryPorts.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
        {
            sb.Append("B:").Append(port.Name).Append(':').Append(port.Direction).Append(':').Append(port.Width).Append('|');
        }

        foreach (HierarchyScopeInstanceViewModel child in scope.ChildScopes.OrderBy(c => c.HierarchyPath, StringComparer.OrdinalIgnoreCase))
        {
            sb.Append("M:").Append(child.HierarchyPath).Append(':').Append(child.ModuleName).Append('|');
            foreach (HierarchyScopeInstancePortConnectionViewModel pin in child.PortConnections
                         .OrderBy(p => p.PortName, StringComparer.OrdinalIgnoreCase))
            {
                sb.Append("P:").Append(pin.PortName).Append(':')
                    .Append(pin.IsInput ? "i" : "o").Append(':')
                    .Append(pin.SignalName).Append(':').Append(pin.Width).Append('|');
            }
        }

        foreach (HierarchyScopeLocalSignalViewModel local in scope.LocalSignals.OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase))
        {
            sb.Append("L:").Append(local.Name).Append(':').Append(local.Width).Append('|');
        }

        foreach (Bistable.Core.Design.DesignContAssign assign in scope.ContAssigns.OrderBy(a => a.TargetName, StringComparer.OrdinalIgnoreCase))
        {
            sb.Append("A:").Append(assign.TargetName).Append(':').Append(assign.OperatorSymbol ?? "");
            if (assign.SourceRange.HasValue)
            {
                sb.Append(':').Append(assign.SourceRange.Value.Hi).Append('-').Append(assign.SourceRange.Value.Lo);
            }

            sb.Append(':');
            foreach (string source in assign.SourceNames.OrderBy(s => s, StringComparer.OrdinalIgnoreCase))
            {
                sb.Append(source).Append(',');
            }

            sb.Append('|');
        }

        byte[] hash = SHA1.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(hash);
    }
}

public sealed record ElkLayoutResult(ElkGraph Graph, IReadOnlyDictionary<string, ElkPortRef> PortRefs);
