using Bistable.App.Services.Routing.Elk;
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
            EmptyScope(primitives: null, first),
            compactLayout: false);
        string secondKey = ElkSchematicEngine.GetCacheKey(
            EmptyScope(primitives: null, second),
            compactLayout: false);

        Assert.NotEqual(firstKey, secondKey);
    }

    private static ElkScopeData EmptyScope(
        IReadOnlyList<SchematicPrimitive>? primitives = null,
        IReadOnlyDictionary<string, IReadOnlyList<SchematicPrimitive>>? catalog = null) =>
        new([], [], [], [], null, primitives, catalog);

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
