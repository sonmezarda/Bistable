using Avalonia.Media;

namespace Bistable.App.Services;

public sealed record SchematicTheme(
    IBrush Background,
    IBrush ModuleFill,
    IBrush ModuleStroke,
    IBrush PinStroke,
    IBrush Selected,
    IBrush ValueFill,
    IBrush InputValue,
    IBrush OutputValue,
    IBrush InactiveInputRoute,
    IBrush InactiveOutputRoute,
    IBrush InactiveLocalRoute,
    IBrush UnknownRoute,
    IBrush Text,
    IBrush Muted,
    IBrush FocusPanelFill,
    IBrush ScopeHighlight,
    IBrush NodeFill,
    IBrush NodeSelectedFill,
    IBrush Connector,
    IBrush LocalNet)
{
    public static readonly SchematicTheme Dark = new(
        Background: B("#10141b"),
        ModuleFill: B("#1b2230"),
        ModuleStroke: B("#344157"),
        PinStroke: B("#57c7ff"),
        Selected: B("#ffd166"),
        ValueFill: B("#121924"),
        InputValue: B("#7fd6ff"),
        OutputValue: B("#65d889"),
        InactiveInputRoute: B("#31516a"),
        InactiveOutputRoute: B("#335d47"),
        InactiveLocalRoute: B("#2d5b55"),
        UnknownRoute: B("#526174"),
        Text: B("#d7dde8"),
        Muted: B("#8f9aad"),
        FocusPanelFill: B("#141b26"),
        ScopeHighlight: B("#2a3a52"),
        NodeFill: B("#192232"),
        NodeSelectedFill: B("#25344a"),
        Connector: B("#4f6487"),
        LocalNet: B("#4fd1b5"));

    public static readonly SchematicTheme Light = new(
        Background: B("#f0f3f8"),
        ModuleFill: B("#e2e8f4"),
        ModuleStroke: B("#8a9cc0"),
        PinStroke: B("#1a7abf"),
        Selected: B("#c77c00"),
        ValueFill: B("#edf0f8"),
        InputValue: B("#1272aa"),
        OutputValue: B("#1a8040"),
        InactiveInputRoute: B("#92b4cc"),
        InactiveOutputRoute: B("#7fb898"),
        InactiveLocalRoute: B("#7ab5ae"),
        UnknownRoute: B("#9aa3b0"),
        Text: B("#1c2438"),
        Muted: B("#6b7893"),
        FocusPanelFill: B("#e8ecf4"),
        ScopeHighlight: B("#cdd8ec"),
        NodeFill: B("#dae1f0"),
        NodeSelectedFill: B("#c8d4e8"),
        Connector: B("#6070a0"),
        LocalNet: B("#0f8070"));

    private static IBrush B(string hex) => SolidColorBrush.Parse(hex);
}
