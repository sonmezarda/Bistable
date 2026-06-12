namespace Bistable.UiTests;

internal static class VisualGoldenGenerator
{
    public static void Main(string[] args)
    {
        HeadlessTestAppBuilder.BuildAvaloniaApp().SetupWithoutStarting();
        string goldenDirectory = ResolveGoldenDirectory();
        Environment.SetEnvironmentVariable("BISTABLE_REGENERATE_VISUALS", "1");
        Environment.SetEnvironmentVariable(
            "BISTABLE_VISUAL_GOLDEN_DIR",
            goldenDirectory);
        Console.WriteLine($"Generating visual goldens in {goldenDirectory}");

        GateSchematicVisualRegressionTests tests = new();
        foreach (object[] testCase in GateSchematicVisualRegressionTests.LodCases)
        {
            Console.WriteLine($"  {testCase[0]}");
            tests.PinLabelLodThresholds_MatchGolden(
                (string)testCase[0],
                (double)testCase[1]);
        }
    }

    private static string ResolveGoldenDirectory()
    {
        return Path.Combine(
            ResolveRepositoryRoot(),
            "tests",
            "Bistable.UiTests",
            "golden");
    }

    private static string ResolveRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Bistable.slnx")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate the verilatorGUI repository root.");
    }
}
