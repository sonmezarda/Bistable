using Bistable.Core.Design;
using Bistable.Core.Projects;

namespace Bistable.App.Services;

public sealed record DesignLoadResult(
    ProjectConfiguration Project,
    ModuleMetadata Metadata,
    string VerilatorVersion,
    string ProjectDirectory);
