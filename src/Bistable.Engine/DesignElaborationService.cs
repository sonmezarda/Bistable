using Bistable.Core.Design;
using Bistable.Core.Design.Ast;
using Bistable.Core.Projects;
using Bistable.Verilator;

namespace Bistable.Engine;

/// <summary>
/// UI-independent project elaboration boundary shared by desktop frontends and
/// the headless engine host.
/// </summary>
public sealed class DesignElaborationService
{
    private readonly ProjectConfigurationValidator _validator = new();
    private readonly VerilatorTool _verilator = new();
    private readonly VerilatorXmlAstReader _astReader = new();

    public async Task<EngineDesignLoadResult> LoadAsync(
        string projectFilePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFilePath);
        string fullProjectPath = Path.GetFullPath(projectFilePath);
        string projectDirectory = Path.GetDirectoryName(fullProjectPath)
            ?? throw new InvalidOperationException("Project file must have a directory.");
        ProjectConfiguration project = await ProjectConfiguration.LoadAsync(fullProjectPath, cancellationToken);
        return await ElaborateAsync(project, projectDirectory, cancellationToken);
    }

    public async Task<EngineDesignLoadResult> ElaborateAsync(
        ProjectConfiguration project,
        string projectDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectDirectory);
        string fullProjectDirectory = Path.GetFullPath(projectDirectory);
        IReadOnlyList<string> errors = _validator.Validate(project, fullProjectDirectory);
        if (errors.Count > 0)
        {
            throw new InvalidDataException(string.Join(Environment.NewLine, errors));
        }

        string outputDirectory = Path.Combine(fullProjectDirectory, ".bistable", "metadata");
        string xmlPath = Path.Combine(outputDirectory, project.TopModule + ".xml");
        await _verilator.GenerateXmlAsync(project, fullProjectDirectory, xmlPath, cancellationToken);
        string version = await _verilator.GetVersionAsync(cancellationToken);

        DesignAst ast = _astReader.Read(xmlPath);
        ElaboratedDesign design = LegacyDesignFlattener.Flatten(ast, project.TopModule);
        return new EngineDesignLoadResult(
            project,
            design,
            design.TopModule,
            version,
            fullProjectDirectory,
            ast);
    }
}
