using Avalonia;
using Avalonia.Headless;
using Bistable.App;
using Bistable.UiTests;

// Tells Avalonia.Headless.XUnit which AppBuilder to use for all tests in this assembly.
// The [AvaloniaFact] attribute relies on this to spin up an isolated headless app
// per test, complete with Dispatcher and rendering pipeline.
[assembly: AvaloniaTestApplication(typeof(HeadlessTestAppBuilder))]

namespace Bistable.UiTests;

public static class HeadlessTestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<BistableApplication>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions
            {
                UseHeadlessDrawing = true,
            });
}
