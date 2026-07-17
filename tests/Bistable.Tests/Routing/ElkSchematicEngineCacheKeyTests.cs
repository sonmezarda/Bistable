using Bistable.App.Services.Routing.Elk;
using Bistable.App.ViewModels;
using Bistable.Core.Design.Schematic;

namespace Bistable.Tests.Routing;

public sealed class ElkSchematicEngineCacheKeyTests
{
    [Fact]
    public async Task ComputeAsync_ReusesCachedLayout_ForSameScope()
    {
        CountingElkRunner runner = new();
        await using SchematicLayoutService service = new(runner);
        ElkSchematicEngine engine = new(service);
        ElkScopeData scope = EmptyScope();

        ElkLayoutResult first = await engine.ComputeAsync(scope, compactLayout: true);
        ElkLayoutResult second = await engine.ComputeAsync(scope, compactLayout: true);

        Assert.Same(first, second);
        Assert.Equal(1, runner.LayoutCalls);
    }

    [Fact]
    public void GetCacheKey_ChangesWhenPrimitiveDefinitionChanges()
    {
        ElkScopeData andScope = EmptyScope([
            new GatePrimitive("gate", "y", GateKind.And, ["a", "b"], 1)
        ]);
        ElkScopeData orScope = EmptyScope([
            new GatePrimitive("gate", "y", GateKind.Or, ["a", "b"], 1)
        ]);

        string andKey = ElkSchematicEngine.GetCacheKey(andScope, compactLayout: true);
        string orKey = ElkSchematicEngine.GetCacheKey(orScope, compactLayout: true);

        Assert.NotEqual(andKey, orKey);
    }

    [Fact]
    public void GetCacheKey_ChangesWhenNestedPrimitiveCatalogChanges()
    {
        Dictionary<string, IReadOnlyList<SchematicPrimitive>> first =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["alu"] = [new BufferPrimitive("buffer", "y", "a", 1)]
            };
        Dictionary<string, IReadOnlyList<SchematicPrimitive>> second =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["alu"] = [new InverterPrimitive("buffer", "y", "a", 1)]
            };

        string firstKey = ElkSchematicEngine.GetCacheKey(
            EmptyScope(primitives: null, first, [Child("alu")]),
            compactLayout: false);
        string secondKey = ElkSchematicEngine.GetCacheKey(
            EmptyScope(primitives: null, second, [Child("alu")]),
            compactLayout: false);

        Assert.NotEqual(firstKey, secondKey);
    }

    [Fact]
    public void GetCacheKey_IgnoresUnrelatedModuleCatalogChanges()
    {
        Dictionary<string, IReadOnlyList<SchematicPrimitive>> first = new(StringComparer.OrdinalIgnoreCase)
        {
            ["alu"] = [new BufferPrimitive("buffer", "y", "a", 1)],
            ["unused"] = [new BufferPrimitive("buffer", "z", "b", 1)]
        };
        Dictionary<string, IReadOnlyList<SchematicPrimitive>> second = new(StringComparer.OrdinalIgnoreCase)
        {
            ["alu"] = [new BufferPrimitive("buffer", "y", "a", 1)],
            ["unused"] = [new InverterPrimitive("buffer", "z", "b", 1)]
        };

        Assert.Equal(
            ElkSchematicEngine.GetCacheKey(EmptyScope(null, first, [Child("alu")]), false),
            ElkSchematicEngine.GetCacheKey(EmptyScope(null, second, [Child("alu")]), false));
    }

    private static ElkScopeData EmptyScope(
        IReadOnlyList<SchematicPrimitive>? primitives = null,
        IReadOnlyDictionary<string, IReadOnlyList<SchematicPrimitive>>? catalog = null,
        IReadOnlyList<HierarchyScopeInstanceViewModel>? children = null) =>
        new([], children ?? [], [], [], null, primitives, catalog);

    private static HierarchyScopeInstanceViewModel Child(string moduleName) => new(
        $"top.u_{moduleName}",
        $"u_{moduleName}",
        moduleName,
        0, 0, 0, 0,
        []);

    private sealed class CountingElkRunner : IElkRunner
    {
        public int LayoutCalls { get; private set; }

        public ElkGraph Layout(ElkGraph input)
        {
            LayoutCalls++;
            return input;
        }

        public void Restart()
        {
        }

        public void Dispose()
        {
        }
    }
}
