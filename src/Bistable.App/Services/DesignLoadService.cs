using Bistable.Core.Projects;
using Bistable.Core.Design;
using Bistable.Core.Design.Ast;
using Bistable.Verilator;

namespace Bistable.App.Services;

public sealed class DesignLoadService
{
    private readonly ProjectConfigurationValidator _validator = new();
    private readonly VerilatorTool _verilator = new();
    private readonly VerilatorXmlAstReader _astReader = new();


    public async Task<DesignLoadResult> LoadAsync(string projectFilePath, CancellationToken cancellationToken)
    {
        string projectDirectory = Path.GetDirectoryName(projectFilePath)
            ?? throw new InvalidOperationException("Project file must have a directory.");

        ProjectConfiguration project = await ProjectConfiguration.LoadAsync(projectFilePath, cancellationToken);
        return await ElaborateAsync(project, projectDirectory, cancellationToken);
    }

    // Elaborate an already-loaded configuration. Used by sub-simulation entry where the
    // outer project file is the same but the TopModule has been overridden in-memory.
    public async Task<DesignLoadResult> ElaborateAsync(
        ProjectConfiguration project,
        string projectDirectory,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<string> errors = _validator.Validate(project, projectDirectory);
        if (errors.Count > 0)
        {
            throw new InvalidDataException(string.Join(Environment.NewLine, errors));
        }

        string outputDirectory = Path.Combine(projectDirectory, ".bistable", "metadata");
        string xmlPath = Path.Combine(outputDirectory, project.TopModule + ".xml");

        await _verilator.GenerateXmlAsync(project, projectDirectory, xmlPath, cancellationToken);
        string version = await _verilator.GetVersionAsync(cancellationToken);

        DesignAst ast = _astReader.Read(xmlPath);
        ElaboratedDesign design = LegacyDesignFlattener.Flatten(ast, project.TopModule);
        return new DesignLoadResult(project, design, design.TopModule, version, projectDirectory, ast);
    }
}
