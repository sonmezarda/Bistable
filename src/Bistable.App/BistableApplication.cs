using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Bistable.App.Services;
using Bistable.App.ViewModels;
using Bistable.App.Views;

namespace Bistable.App;

public sealed class BistableApplication : Application
{
    public override void Initialize()
    {
        RequestedThemeVariant = ThemeVariant.Dark;
        Styles.Add(new FluentTheme());
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            BistableWorkspace workspace = new(
                new ProjectDialogService(),
                new DesignLoadService(),
                new Bistable.Verilator.SimulationWorkerBuilder(),
                new PreviewSimulationService(),
                new LayoutStateService());

            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel(workspace)
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
