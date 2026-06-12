using Bistable.App.Services.Routing.Elk;
using Bistable.Core.Projects;
using Bistable.Core.Synthesis;

namespace Bistable.Tests.Routing;

public sealed class GateLevelLayoutCacheTests
{
    [Fact]
    public void CreateKey_ExpandedPathOrderingDoesNotChangeIdentity()
    {
        GateLevelLayoutCache cache = new(CreateNetlist("$_AND_"));

        GateLevelLayoutCacheKey first = cache.CreateKey(
            ["top"],
            new HashSet<string>(["u_b", "u_a"], StringComparer.Ordinal),
            RoutingQuality.FastPreview,
            autoDowngradeLargeGraphs: true);
        GateLevelLayoutCacheKey second = cache.CreateKey(
            ["top"],
            new HashSet<string>(["u_a", "u_b"], StringComparer.Ordinal),
            RoutingQuality.FastPreview,
            autoDowngradeLargeGraphs: true);

        Assert.Equal(first, second);
    }

    [Fact]
    public void CreateKey_RoutingInputsChangeIdentity()
    {
        GateLevelLayoutCache cache = new(CreateNetlist("$_AND_"));
        GateLevelLayoutCacheKey baseline = cache.CreateKey(
            ["top"],
            new HashSet<string>(StringComparer.Ordinal),
            RoutingQuality.Balanced,
            autoDowngradeLargeGraphs: true);

        Assert.NotEqual(
            baseline,
            cache.CreateKey(
                ["top", "u_child"],
                new HashSet<string>(StringComparer.Ordinal),
                RoutingQuality.Balanced,
                autoDowngradeLargeGraphs: true));
        Assert.NotEqual(
            baseline,
            cache.CreateKey(
                ["top"],
                new HashSet<string>(StringComparer.Ordinal),
                RoutingQuality.Production,
                autoDowngradeLargeGraphs: true));
        Assert.NotEqual(
            baseline,
            cache.CreateKey(
                ["top"],
                new HashSet<string>(StringComparer.Ordinal),
                RoutingQuality.Balanced,
                autoDowngradeLargeGraphs: false));
    }

    [Fact]
    public void Fingerprint_ChangesWhenSynthesizedCellChanges()
    {
        string andFingerprint =
            GateLevelLayoutCache.ComputeNetlistFingerprint(CreateNetlist("$_AND_"));
        string orFingerprint =
            GateLevelLayoutCache.ComputeNetlistFingerprint(CreateNetlist("$_OR_"));

        Assert.NotEqual(andFingerprint, orFingerprint);
    }

    [Fact]
    public void Store_RecentlyReadEntrySurvivesCapacityEviction()
    {
        GateLevelLayoutCache cache = new(
            CreateNetlist("$_AND_"),
            capacity: 2,
            complexityBudget: 100);
        GateLevelLayoutCacheKey first = Key(cache, "first");
        GateLevelLayoutCacheKey second = Key(cache, "second");
        GateLevelLayoutCacheKey third = Key(cache, "third");

        cache.Store(first, Entry(complexity: 3));
        cache.Store(second, Entry(complexity: 3));
        Assert.True(cache.TryGet(first, out _));
        cache.Store(third, Entry(complexity: 3));

        Assert.True(cache.TryGet(first, out _));
        Assert.False(cache.TryGet(second, out _));
        Assert.True(cache.TryGet(third, out _));
    }

    [Fact]
    public void Store_ComplexityBudgetEvictsOldestEntry()
    {
        GateLevelLayoutCache cache = new(
            CreateNetlist("$_AND_"),
            capacity: 10,
            complexityBudget: 10);
        GateLevelLayoutCacheKey first = Key(cache, "first");
        GateLevelLayoutCacheKey second = Key(cache, "second");

        cache.Store(first, Entry(complexity: 6));
        cache.Store(second, Entry(complexity: 6));

        Assert.Equal(1, cache.Count);
        Assert.False(cache.TryGet(first, out _));
        Assert.True(cache.TryGet(second, out _));
    }

    [Fact]
    public void Store_SingleOversizedLayoutRemainsCacheable()
    {
        GateLevelLayoutCache cache = new(
            CreateNetlist("$_AND_"),
            capacity: 3,
            complexityBudget: 10);
        GateLevelLayoutCacheKey key = Key(cache, "large");
        GateLevelLayoutCacheEntry entry = Entry(complexity: 20);

        cache.Store(key, entry);

        Assert.Equal(1, cache.Count);
        Assert.True(cache.TryGet(key, out GateLevelLayoutCacheEntry? cached));
        Assert.Same(entry, cached);
    }

    private static GateLevelLayoutCacheKey Key(
        GateLevelLayoutCache cache,
        string scope) =>
        cache.CreateKey(
            ["top", scope],
            new HashSet<string>(StringComparer.Ordinal),
            RoutingQuality.FastPreview,
            autoDowngradeLargeGraphs: true);

    private static GateLevelLayoutCacheEntry Entry(int complexity)
    {
        SchematicGraphMetrics metrics = new(
            NodeCount: complexity,
            PortCount: 0,
            EdgeCount: 0);
        return new GateLevelLayoutCacheEntry(
            new ElkGraph(),
            new SchematicLayoutDecision(RoutingQuality.FastPreview, false, metrics),
            metrics,
            []);
    }

    private static GateNetlist CreateNetlist(string cellType)
    {
        GateModule top = new(
            "top",
            [
                new GatePort("a", GatePortDirection.Input, [GateBit.Net(2)]),
                new GatePort("y", GatePortDirection.Output, [GateBit.Net(3)]),
            ],
            [
                new GateCell(
                    "u_gate",
                    cellType,
                    new Dictionary<string, GateConnection>
                    {
                        ["A"] = new("A", [GateBit.Net(2)]),
                        ["Y"] = new("Y", [GateBit.Net(3)]),
                    },
                    new Dictionary<string, GatePortDirection>
                    {
                        ["A"] = GatePortDirection.Input,
                        ["Y"] = GatePortDirection.Output,
                    },
                    new Dictionary<string, string>(),
                    new Dictionary<string, string>()),
            ],
            [new GateNet("y", [GateBit.Net(3)])]);
        return new GateNetlist(
            "top",
            new Dictionary<string, GateModule> { ["top"] = top });
    }
}
