using Avalonia;
using Avalonia.Media.Imaging;

namespace Bistable.UiTests;

internal static class VisualGoldenAssert
{
    private const string RegenerateEnvironmentVariable =
        "BISTABLE_REGENERATE_VISUALS";
    private const string GoldenDirectoryEnvironmentVariable =
        "BISTABLE_VISUAL_GOLDEN_DIR";

    public static void Matches(
        string name,
        RenderTargetBitmap rendered,
        PixelRect crop)
    {
        string goldenDirectory = ResolveGoldenDirectory();
        string goldenPath = Path.Combine(goldenDirectory, name + ".png");
        string actualPath = Path.Combine(goldenDirectory, name + ".actual.png");
        using RenderTargetBitmap cropped = Crop(rendered, crop);

        if (Environment.GetEnvironmentVariable(RegenerateEnvironmentVariable) == "1")
        {
            Directory.CreateDirectory(goldenDirectory);
            File.WriteAllBytes(goldenPath, EncodePng(cropped));
            TryDelete(actualPath);
            return;
        }

        if (!File.Exists(goldenPath))
        {
            Directory.CreateDirectory(goldenDirectory);
            File.WriteAllBytes(actualPath, EncodePng(cropped));
            throw new Xunit.Sdk.XunitException(
                $"Visual golden '{name}' is missing. Actual written to {actualPath}. "
                + $"Set {RegenerateEnvironmentVariable}=1 and rerun to create it.");
        }

        byte[] expected = File.ReadAllBytes(goldenPath);
        byte[] actual = EncodePng(cropped);
        if (expected.AsSpan().SequenceEqual(actual))
        {
            TryDelete(actualPath);
            return;
        }

        File.WriteAllBytes(actualPath, actual);
        throw new Xunit.Sdk.XunitException(
            $"Visual golden '{name}' diverged. Actual written to {actualPath}. "
            + $"Review the PNGs before regenerating with {RegenerateEnvironmentVariable}=1.");
    }

    private static RenderTargetBitmap Crop(
        RenderTargetBitmap source,
        PixelRect crop)
    {
        RenderTargetBitmap result = new(crop.Size);
        using Avalonia.Media.DrawingContext context = result.CreateDrawingContext();
        context.DrawImage(
            source,
            new Rect(crop.X, crop.Y, crop.Width, crop.Height),
            new Rect(0, 0, crop.Width, crop.Height));
        return result;
    }

    private static byte[] EncodePng(RenderTargetBitmap bitmap)
    {
        using MemoryStream stream = new();
        bitmap.Save(stream);
        return stream.ToArray();
    }

    private static string ResolveGoldenDirectory()
    {
        string? configuredDirectory =
            Environment.GetEnvironmentVariable(GoldenDirectoryEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(configuredDirectory))
        {
            return Path.GetFullPath(configuredDirectory);
        }

        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(
                directory.FullName,
                "tests",
                "Bistable.UiTests",
                "golden");
            if (Directory.Exists(Path.GetDirectoryName(candidate)))
            {
                return candidate;
            }
            directory = directory.Parent;
        }

        return Path.Combine(AppContext.BaseDirectory, "golden");
    }

    private static void TryDelete(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
