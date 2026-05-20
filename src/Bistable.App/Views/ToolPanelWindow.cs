using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Bistable.App.ViewModels;

namespace Bistable.App.Views;

public sealed class ToolPanelWindow : Window
{
    private static readonly IBrush BackgroundBrush = SolidColorBrush.Parse("#0e1116");

    public ToolPanelWindow(DockPanelKind panelKind, string title, Control content)
    {
        PanelKind = panelKind;
        Title = $"Bistable {title}";
        Width = panelKind == DockPanelKind.Project ? 520 : 1120;
        Height = panelKind == DockPanelKind.Project ? 760 : 820;
        MinWidth = panelKind == DockPanelKind.Project ? 420 : 720;
        MinHeight = 520;
        Background = BackgroundBrush;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = content;
    }

    public DockPanelKind PanelKind { get; }
}
