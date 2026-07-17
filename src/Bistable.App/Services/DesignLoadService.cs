using Bistable.Core.Projects;
using Bistable.Engine;

namespace Bistable.App.Services;

public sealed class DesignLoadService
{
    private readonly DesignElaborationService _engine = new();

    public async Task<DesignLoadResult> LoadAsync(string projectFilePath, CancellationToken cancellationToken)
    {
        EngineDesignLoadResult result = await _engine.LoadAsync(projectFilePath, cancellationToken);
        return Map(result);
    }

    // Elaborate an already-loaded configuration. Used by sub-simulation entry where the
    // outer project file is the same but the TopModule has been overridden in-memory.
    public async Task<DesignLoadResult> ElaborateAsync(
        ProjectConfiguration project,
        string projectDirectory,
        CancellationToken cancellationToken)
    {
        EngineDesignLoadResult result = await _engine.ElaborateAsync(project, projectDirectory, cancellationToken);
        return Map(result);
    }

    private static DesignLoadResult Map(EngineDesignLoadResult result) => new(
        result.Project,
        result.Design,
        result.Metadata,
        result.VerilatorVersion,
        result.ProjectDirectory,
        result.Ast);
}
