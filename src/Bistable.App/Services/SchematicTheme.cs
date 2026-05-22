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
    IBrush LocalNet,
    // Logisim-Evolution-style state palette: colour reflects the *signal value*,
    // direction is conveyed by glyph instead. Used by the ELK render path.
    IBrush LogicLow,
    IBrush LogicHigh,
    IBrush BusActive,
    IBrush BusInactive,
    IBrush Unknown,
    IBrush HighZ)
{
    // Logisim-Evolution canonical colours — defined once so legacy fields can alias
    // onto them without scattering the same literal across the file.
    private const string DarkLogicLow = "#3F7F3F";
    private const string DarkLogicHigh = "#6BE56B";
    private const string DarkBusActive = "#E0E2EA";
    private const string DarkBusInactive = "#7A8499";
    private const string DarkUnknown = "#FF6B6B";
    private const string DarkHighZ = "#5BC0FF";

    private const string LightLogicLow = "#86b886";
    private const string LightLogicHigh = "#1c7a1c";
    private const string LightBusActive = "#3a4356";
    private const string LightBusInactive = "#9aa3b0";
    private const string LightUnknown = "#cc2e2e";
    private const string LightHighZ = "#1f6dc4";

    public static readonly SchematicTheme Dark = new(
        Background: B("#10141b"),
        ModuleFill: B("#1b2230"),
        ModuleStroke: B("#344157"),
        // Legacy fields aliased onto the new Logisim palette so the older
        // SchematicMazeRouter render path stays visually consistent.
        PinStroke: B(DarkLogicHigh),
        Selected: B("#FFD166"),
        ValueFill: B("#121924"),
        InputValue: B(DarkLogicHigh),
        OutputValue: B(DarkLogicHigh),
        InactiveInputRoute: B(DarkLogicLow),
        InactiveOutputRoute: B(DarkLogicLow),
        InactiveLocalRoute: B(DarkBusInactive),
        UnknownRoute: B(DarkUnknown),
        Text: B("#d7dde8"),
        Muted: B("#8f9aad"),
        FocusPanelFill: B("#141b26"),
        ScopeHighlight: B("#2a3a52"),
        NodeFill: B("#192232"),
        NodeSelectedFill: B("#25344a"),
        Connector: B("#4f6487"),
        LocalNet: B(DarkBusActive),
        LogicLow: B(DarkLogicLow),
        LogicHigh: B(DarkLogicHigh),
        BusActive: B(DarkBusActive),
        BusInactive: B(DarkBusInactive),
        Unknown: B(DarkUnknown),
        HighZ: B(DarkHighZ));

    public static readonly SchematicTheme Light = new(
        Background: B("#f0f3f8"),
        ModuleFill: B("#e2e8f4"),
        ModuleStroke: B("#8a9cc0"),
        PinStroke: B(LightLogicHigh),
        Selected: B("#c77c00"),
        ValueFill: B("#edf0f8"),
        InputValue: B(LightLogicHigh),
        OutputValue: B(LightLogicHigh),
        InactiveInputRoute: B(LightLogicLow),
        InactiveOutputRoute: B(LightLogicLow),
        InactiveLocalRoute: B(LightBusInactive),
        UnknownRoute: B(LightUnknown),
        Text: B("#1c2438"),
        Muted: B("#6b7893"),
        FocusPanelFill: B("#e8ecf4"),
        ScopeHighlight: B("#cdd8ec"),
        NodeFill: B("#dae1f0"),
        NodeSelectedFill: B("#c8d4e8"),
        Connector: B("#6070a0"),
        LocalNet: B(LightBusActive),
        LogicLow: B(LightLogicLow),
        LogicHigh: B(LightLogicHigh),
        BusActive: B(LightBusActive),
        BusInactive: B(LightBusInactive),
        Unknown: B(LightUnknown),
        HighZ: B(LightHighZ));

    private static IBrush B(string hex) => SolidColorBrush.Parse(hex);
}
