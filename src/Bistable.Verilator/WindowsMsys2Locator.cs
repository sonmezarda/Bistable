namespace Bistable.Verilator;

internal static class WindowsMsys2Locator
{
    private static readonly string[] SearchRoots =
    [
        @"C:\msys64",
        @"C:\tools\msys64",
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\msys64"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), @"scoop\apps\msys2\current")
    ];

    public static Msys2Paths? Detect()
    {
        foreach (string root in SearchRoots)
        {
            string bin = Path.Combine(root, @"ucrt64\bin\verilator_bin.exe");
            if (!File.Exists(bin))
            {
                continue;
            }

            return new Msys2Paths(
                VerilatorExecutable: bin,
                ExtraPath: $@"{root}\ucrt64\bin;{root}\usr\bin",
                VerilatorRoot: Path.Combine(root, @"ucrt64\share\verilator"));
        }

        return null;
    }

    public sealed record Msys2Paths(string VerilatorExecutable, string ExtraPath, string VerilatorRoot);
}
