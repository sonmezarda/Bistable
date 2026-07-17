using Bistable.Core.Design;
using Bistable.Core.Design.Ast;
using Bistable.Core.Projects;

namespace Bistable.Engine;

public sealed record EngineDesignLoadResult(
    ProjectConfiguration Project,
    ElaboratedDesign Design,
    ModuleMetadata Metadata,
    string VerilatorVersion,
    string ProjectDirectory,
    DesignAst Ast);
