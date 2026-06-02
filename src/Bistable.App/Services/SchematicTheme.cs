using Avalonia.Media;

namespace Bistable.App.Services;

// P2.7-9: discrete preset identifiers persisted in user settings + bound to the
// theme combo box. Adding a new preset = add an enum value AND a static field
// on SchematicTheme + an entry in SchematicThemePresets.Get.
public enum SchematicThemePreset
{
    Dark,
    Light,
    HighContrast,
    Print,
}

public static class SchematicThemePresets
{
    public static SchematicTheme Get(SchematicThemePreset preset) => preset switch
    {
        SchematicThemePreset.Dark => SchematicTheme.Dark,
        SchematicThemePreset.Light => SchematicTheme.Light,
        SchematicThemePreset.HighContrast => SchematicTheme.HighContrast,
        SchematicThemePreset.Print => SchematicTheme.Print,
        _ => SchematicTheme.Dark,
    };

    public static string DisplayName(SchematicThemePreset preset) => preset switch
    {
        SchematicThemePreset.Dark => "Dark",
        SchematicThemePreset.Light => "Light",
        SchematicThemePreset.HighContrast => "High contrast",
        SchematicThemePreset.Print => "Print-friendly",
        _ => preset.ToString(),
    };
}

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

    // High-contrast canonical colours.
    private const string HcBlack = "#000000";
    private const string HcWhite = "#ffffff";
    private const string HcGreen = "#00ff00";
    private const string HcDimGray = "#808080";
    private const string HcRed = "#ff3030";
    private const string HcYellow = "#ffff00";
    private const string HcCyan = "#00d0ff";
    private const string HcDarkGray = "#202020";
    private const string HcMidGray = "#303030";
    private const string HcMutedGray = "#bcbcbc";

    // Print-friendly canonical colours (grayscale).
    private const string PrWhite = "#ffffff";
    private const string PrBlack = "#000000";
    private const string PrDimGray = "#808080";
    private const string PrLightGray = "#a0a0a0";
    private const string PrMidGray = "#606060";
    private const string PrSelGray = "#404040";
    private const string PrPanelBg = "#f4f4f4";
    private const string PrScopeHi = "#dadada";
    private const string PrNodeSel = "#e0e0e0";

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

    // P2.7-9: High-contrast — black background, pure-white / saturated strokes
    // designed for the WCAG 2.x AAA contrast ratio. Same Logisim-derived state
    // colours but pushed to the brightest variant so they pop against pure black.
    public static readonly SchematicTheme HighContrast = new(
        Background: B(HcBlack),
        ModuleFill: B(HcBlack),
        ModuleStroke: B(HcWhite),
        PinStroke: B(HcGreen),
        Selected: B(HcYellow),
        ValueFill: B(HcBlack),
        InputValue: B(HcGreen),
        OutputValue: B(HcGreen),
        InactiveInputRoute: B(HcDimGray),
        InactiveOutputRoute: B(HcDimGray),
        InactiveLocalRoute: B(HcDimGray),
        UnknownRoute: B(HcRed),
        Text: B(HcWhite),
        Muted: B(HcMutedGray),
        FocusPanelFill: B(HcBlack),
        ScopeHighlight: B(HcDarkGray),
        NodeFill: B(HcBlack),
        NodeSelectedFill: B(HcMidGray),
        Connector: B(HcWhite),
        LocalNet: B(HcWhite),
        LogicLow: B(HcDimGray),
        LogicHigh: B(HcGreen),
        BusActive: B(HcWhite),
        BusInactive: B(HcDimGray),
        Unknown: B(HcRed),
        HighZ: B(HcCyan));

    // P2.7-9: Print-friendly — white background, black strokes, monochrome state
    // palette so screenshots printed on a monochrome printer remain legible.
    // Active/inactive distinguished by grayscale value, not hue.
    public static readonly SchematicTheme Print = new(
        Background: B(PrWhite),
        ModuleFill: B(PrWhite),
        ModuleStroke: B(PrBlack),
        PinStroke: B(PrBlack),
        Selected: B(PrSelGray),
        ValueFill: B(PrWhite),
        InputValue: B(PrBlack),
        OutputValue: B(PrBlack),
        InactiveInputRoute: B(PrDimGray),
        InactiveOutputRoute: B(PrDimGray),
        InactiveLocalRoute: B(PrLightGray),
        UnknownRoute: B(PrSelGray),
        Text: B(PrBlack),
        Muted: B(PrMidGray),
        FocusPanelFill: B(PrPanelBg),
        ScopeHighlight: B(PrScopeHi),
        NodeFill: B(PrWhite),
        NodeSelectedFill: B(PrNodeSel),
        Connector: B(PrBlack),
        LocalNet: B(PrBlack),
        LogicLow: B(PrLightGray),
        LogicHigh: B(PrBlack),
        BusActive: B(PrBlack),
        BusInactive: B(PrDimGray),
        Unknown: B(PrMidGray),
        HighZ: B(PrSelGray));

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
