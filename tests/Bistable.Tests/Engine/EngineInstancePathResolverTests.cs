using Bistable.Core.Design.Ast;
using Bistable.Engine;

namespace Bistable.Tests.Engine;

/// <summary>
/// The hierarchical instance path — never the module type name — identifies a
/// schematic document. These tests lock the resolution rules the Theia
/// hierarchy navigation depends on.
/// </summary>
public sealed class EngineInstancePathResolverTests
{
    private static ModuleAst Module(string name, bool isTop = false, params InstanceDecl[] instances) =>
        new(name, isTop, [], [], [], instances, [], [], []);

    private static DesignAst Design()
    {
        ModuleAst alu = Module("riscv_alu");
        ModuleAst core = Module("riscv_core", instances: [new InstanceDecl("u_alu", "riscv_alu", [])]);
        ModuleAst top = Module("top", isTop: true, instances:
        [
            new InstanceDecl("u_core", "riscv_core", []),
            new InstanceDecl("u_alu", "riscv_alu", [])
        ]);
        return new DesignAst([top, core, alu]);
    }

    [Fact]
    public void Resolve_TopOnlyPath_ReturnsTopModule()
    {
        ModuleAst resolved = EngineInstancePathResolver.Resolve(Design(), "top");
        Assert.Equal("top", resolved.Name);
        Assert.True(resolved.IsTop);
    }

    [Fact]
    public void Resolve_DirectInstance_ReturnsInstantiatedModule()
    {
        Assert.Equal("riscv_alu", EngineInstancePathResolver.Resolve(Design(), "top.u_alu").Name);
    }

    [Fact]
    public void Resolve_NestedInstances_FollowsEachSegment()
    {
        Assert.Equal("riscv_alu", EngineInstancePathResolver.Resolve(Design(), "top.u_core.u_alu").Name);
    }

    [Fact]
    public void Resolve_TwoPathsToSameModuleType_BothResolveIndependently()
    {
        // Distinct instance paths of the same module type are distinct
        // documents; both must resolve without interfering with each other.
        DesignAst design = Design();
        Assert.Equal(
            EngineInstancePathResolver.Resolve(design, "top.u_alu").Name,
            EngineInstancePathResolver.Resolve(design, "top.u_core.u_alu").Name);
    }

    [Fact]
    public void Resolve_UnknownInstance_ThrowsWithFullPathContext()
    {
        InvalidInstancePathException exception = Assert.Throws<InvalidInstancePathException>(
            () => EngineInstancePathResolver.Resolve(Design(), "top.u_missing"));
        Assert.Contains("u_missing", exception.Message);
        Assert.Contains("top.u_missing", exception.Message);
    }

    [Fact]
    public void Resolve_PathNotStartingAtTop_IsRejected()
    {
        // A module type name is not an instance path.
        Assert.Throws<InvalidInstancePathException>(
            () => EngineInstancePathResolver.Resolve(Design(), "riscv_core.u_alu"));
    }

    [Fact]
    public void Resolve_EmptyPath_IsRejected()
    {
        Assert.Throws<InvalidInstancePathException>(
            () => EngineInstancePathResolver.Resolve(Design(), " "));
    }

    [Fact]
    public void Resolve_InstanceNamesAreCaseSensitive()
    {
        Assert.Throws<InvalidInstancePathException>(
            () => EngineInstancePathResolver.Resolve(Design(), "top.U_ALU"));
    }

    [Fact]
    public void Resolve_TopSegmentMatchesOriginalSourceName()
    {
        // Verilator may rename a module (e.g. parametrization); the original
        // source name must keep resolving for display-driven navigation.
        ModuleAst top = new("top__P1", true, [], [], [], [], [], [], [], OriginalName: "top");
        DesignAst design = new([top]);
        Assert.Same(top, EngineInstancePathResolver.Resolve(design, "top"));
    }
}
