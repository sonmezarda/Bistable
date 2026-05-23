using Bistable.Core.Design;
using Bistable.Core.Design.Ast;
using Bistable.Core.Projects;

namespace Bistable.App.Services;

public sealed record DesignLoadResult(
    ProjectConfiguration Project,
    ElaboratedDesign Design,
    ModuleMetadata Metadata,
    string VerilatorVersion,
    string ProjectDirectory,
    DesignAst? Ast = null);
