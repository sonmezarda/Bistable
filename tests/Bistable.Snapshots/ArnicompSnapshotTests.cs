using System.Diagnostics;
using System.IO;
using Bistable.App.Services;
using Bistable.App.Services.Routing.Elk;
using Bistable.App.ViewModels;
using Bistable.Core.Design;
using Bistable.Core.Design.Ast;
using Bistable.Core.Design.Schematic;

namespace Bistable.Snapshots;

/// <summary>
/// Phase 2 P2-9: end-to-end golden snapshots built from the real samples/arnicomp project.
/// These tests require Verilator to elaborate the design and are skipped when it is absent.
///
/// Regenerate with: BISTABLE_REGENERATE_SNAPSHOTS=1 dotnet test tests/Bistable.Snapshots --filter "Category=Integration"
/// </summary>
[Trait("Category", "Integration")]
[Trait("RequiresVerilator", "true")]
public sealed class ArnicompSnapshotTests
{
    // ── skip guard ───────────────────────────────────────────────────────────

    private static bool HasVerilator()
    {
        try
        {
            ProcessStartInfo psi = new("which", "verilator") { RedirectStandardOutput = true };
            Process? p = Process.Start(psi);
            p!.WaitForExit(2000);
            return p.ExitCode == 0;
        }
        catch { return false; }
    }

    private static string ResolveProjectPath()
    {
        // Walk up from the test output directory until we find samples/arnicomp.
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string candidate = Path.Combine(dir.FullName, "samples", "arnicomp", "arnicomp.bistable.json");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException("Could not locate samples/arnicomp/arnicomp.bistable.json from test output dir.");
    }

    // ── snapshot 1: arnicomp top scope, collapsed ─────────────────────────────

    [Fact]
    public async Task Snapshot_ArnicompTop_Collapsed()
    {
        if (!HasVerilator()) return;

        DesignLoadResult result = await new DesignLoadService().LoadAsync(ResolveProjectPath(), CancellationToken.None);
        ModuleAst top = result.Ast!.TopModule!;

        ElkBuildResult elk = BuildElk(top, result.Ast!, result.Design, expandedPaths: null);
        SnapshotAssert.MatchesElkGraph("arnicomp-top", elk.Graph);
    }

    // ── snapshot 2: top scope with marl_i expanded (leaf module with FFs) ───

    [Fact]
    public async Task Snapshot_ArnicompTop_ExpandedMarlI()
    {
        if (!HasVerilator()) return;

        DesignLoadResult result = await new DesignLoadService().LoadAsync(ResolveProjectPath(), CancellationToken.None);
        ModuleAst top = result.Ast!.TopModule!;

        // reg_marl is a leaf module (no sub-instances). P2-8b enables expanding it
        // to reveal the inner FF that implements the 16-bit address register.
        string topName = top.Name;  // "arnicomp_top"
        var expanded = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            topName + ".marl_i"
        };

        ElkBuildResult elk = BuildElk(top, result.Ast!, result.Design, expandedPaths: expanded);
        SnapshotAssert.MatchesElkGraph("arnicomp-top-expanded-marl_i", elk.Graph);
    }

    // ── snapshot 3: acc module (reg_cell) — FF + contassign ─────────────────

    [Fact]
    public async Task Snapshot_ArnicompRegCell()
    {
        if (!HasVerilator()) return;

        DesignLoadResult result = await new DesignLoadService().LoadAsync(ResolveProjectPath(), CancellationToken.None);

        // "acc" is an instance of module "reg_cell" in arnicomp_top.
        // Render the reg_cell scope directly (as if the user selected it in the hierarchy).
        ModuleAst? regCell = result.Ast!.Modules.FirstOrDefault(m =>
            string.Equals(m.Name, "reg_cell", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(regCell);

        ElkBuildResult elk = BuildElk(regCell!, result.Ast!, result.Design, expandedPaths: null);
        SnapshotAssert.MatchesElkGraph("arnicomp-reg-cell", elk.Graph);
    }

    // ── snapshot 4: arnicomp_top with flag_reg_i expanded (1 level) ─────────
    //
    // P4.5-8: locks the synthetic joiner / fan-out wiring around flag_register's
    // concat-bound pins (`d({z,n,c,v})`, `out({z,n,c,v})`). Before P4.5-12 these
    // edges silently vanished — this snapshot guards against regressions in the
    // concat parser + builder bundle-node code path.
    [Fact]
    public async Task Snapshot_ArnicompTop_ExpandedFlagRegI()
    {
        if (!HasVerilator()) return;

        DesignLoadResult result = await new DesignLoadService().LoadAsync(ResolveProjectPath(), CancellationToken.None);
        ModuleAst top = result.Ast!.TopModule!;

        string topName = top.Name;
        var expanded = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            topName + ".flag_reg_i"
        };

        ElkBuildResult elk = BuildElk(top, result.Ast!, result.Design, expandedPaths: expanded);
        SnapshotAssert.MatchesElkGraph("arnicomp-top-expanded-flag_reg_i", elk.Graph);
    }

    // ── snapshot 5: arnicomp_top with flag_reg_i AND flag_register expanded ─
    //
    // P4.5-8: the same wiring, but two compounds deep. Verifies that the inner
    // namespace recursion threads concat bundles through every level — this was
    // the user-reported "hiçbir wire göremedim" regression for nested expand.
    [Fact]
    public async Task Snapshot_ArnicompTop_ExpandedFlagRegI_AndFlagRegister()
    {
        if (!HasVerilator()) return;

        DesignLoadResult result = await new DesignLoadService().LoadAsync(ResolveProjectPath(), CancellationToken.None);
        ModuleAst top = result.Ast!.TopModule!;

        string topName = top.Name;
        var expanded = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            topName + ".flag_reg_i",
            topName + ".flag_reg_i.flag_register",
        };

        ElkBuildResult elk = BuildElk(top, result.Ast!, result.Design, expandedPaths: expanded);
        SnapshotAssert.MatchesElkGraph("arnicomp-top-expanded-flag_reg_i-and-flag_register", elk.Graph);
    }

    // ── scope builder ──────────────────────────────────────────────────────

    /// <summary>
    /// Builds <see cref="ElkScopeData"/> from a module AST and runs <see cref="ElkGraphBuilder"/>.
    /// This is a minimal port of the MainWindowViewModel scope-assembly logic, sufficient
    /// for snapshot tests (no trace/signal state, no VM hierarchy navigation needed).
    /// </summary>
    private static ElkBuildResult BuildElk(
        ModuleAst module,
        DesignAst ast,
        ElaboratedDesign flat,
        IReadOnlySet<string>? expandedPaths)
    {
        // Build a module catalog keyed by name for port-width resolution.
        Dictionary<string, ModuleAst> moduleCatalog = ast.Modules.ToDictionary(
            m => m.Name, StringComparer.OrdinalIgnoreCase);

        // Boundary ports (the selected scope's own ports).
        IReadOnlyList<HierarchyScopePortViewModel> boundaryPorts = module.Ports
            .OrderBy(p => p.PinIndex)
            .Select(p => new HierarchyScopePortViewModel(p.Name, p.Direction, p.Width, p.IsSigned))
            .ToList();

        // Child scope instances (one level deep — ElkGraphBuilder handles inner expansion).
        string parentPath = module.Name;
        IReadOnlyList<HierarchyScopeInstanceViewModel> childScopes = BuildChildScopes(
            module, moduleCatalog, parentPath, expandedPaths, flat);

        // Local signals (widths only — no live values in snapshot mode).
        IReadOnlyList<HierarchyScopeLocalSignalViewModel> localSignals = module.LocalSignals
            .Select(s => new HierarchyScopeLocalSignalViewModel(s.Name, s.Width, s.IsSigned, false, "", null))
            .ToList();

        // Cont assigns from the flat design (legacy path for splitters/joiners/operators).
        IReadOnlyList<DesignContAssign> contAssigns = flat.ModuleDefinitions.TryGetValue(
            module.Name, out DesignModuleDefinition? def) ? def.ContAssigns : [];

        // Primitives for the selected scope.
        SchematicPrimitiveList primitives = SchematicDecoder.Decode(module);

        // Decode primitives for every module in the design (for expanded-compound interiors).
        IReadOnlyDictionary<string, IReadOnlyList<SchematicPrimitive>> primitivesByModule =
            ast.Modules.ToDictionary(
                m => m.Name,
                m => (IReadOnlyList<SchematicPrimitive>)SchematicDecoder.Decode(m).Logic,
                StringComparer.OrdinalIgnoreCase);

        return new ElkGraphBuilder().Build(
            new ElkScopeData(
                boundaryPorts,
                childScopes,
                localSignals,
                contAssigns,
                expandedPaths,
                primitives.Logic,
                primitivesByModule),
            compactLayout: true);
    }

    private static IReadOnlyList<HierarchyScopeInstanceViewModel> BuildChildScopes(
        ModuleAst parent,
        IReadOnlyDictionary<string, ModuleAst> moduleCatalog,
        string parentPath,
        IReadOnlySet<string>? expandedPaths,
        ElaboratedDesign flat)
    {
        return parent.Instances
            .Select(inst => BuildChildScope(inst, moduleCatalog, parentPath, expandedPaths, flat))
            .ToList();
    }

    private static HierarchyScopeInstanceViewModel BuildChildScope(
        InstanceDecl inst,
        IReadOnlyDictionary<string, ModuleAst> moduleCatalog,
        string parentPath,
        IReadOnlySet<string>? expandedPaths,
        ElaboratedDesign flat)
    {
        string path = parentPath + "." + inst.InstanceName;
        moduleCatalog.TryGetValue(inst.ModuleName, out ModuleAst? subModule);

        // Build port connections with widths from the sub-module's port declarations.
        List<HierarchyScopeInstancePortConnectionViewModel> ports = [];
        foreach (PortConnectionDecl pc in inst.PortConnections)
        {
            int width = 1;
            if (subModule is not null)
            {
                PortDecl? portDecl = subModule.Ports.FirstOrDefault(
                    p => string.Equals(p.Name, pc.PortName, StringComparison.OrdinalIgnoreCase));
                if (portDecl is not null) width = portDecl.Width;
            }
            bool isInput = string.Equals(pc.Direction, "in", StringComparison.OrdinalIgnoreCase);
            // P4.5: thread ConcatParts through so concat-bundled pins like
            // `flag_register.d({z_f_in,n_f_in,c_f_in,v_f_in})` register through
            // the synthetic joiner/fan-out nodes in the snapshot graph too.
            ports.Add(new HierarchyScopeInstancePortConnectionViewModel(
                pc.PortName, pc.SignalName, isInput, width, pc.ConcatParts));
        }

        // Recurse into children when this instance is in the expanded set and has sub-instances.
        // Pass the FULL module catalog through so port widths resolve at every depth.
        IReadOnlyList<HierarchyScopeInstanceViewModel>? children = null;
        if (expandedPaths is not null && expandedPaths.Contains(path) && subModule is not null
            && subModule.Instances.Count > 0)
        {
            children = BuildChildScopes(subModule, moduleCatalog, path, expandedPaths, flat);
        }

        return new HierarchyScopeInstanceViewModel(
            path, inst.InstanceName, inst.ModuleName,
            ports.Count(p => p.IsInput), ports.Count(p => p.IsOutput),
            0, 0, ports, childInstances: children);
    }
}
