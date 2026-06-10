using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Bistable.App.Services.Routing.Elk;
using Bistable.Core.Projects;
using Bistable.Core.Synthesis;

namespace Bistable.App.Views;

/// <summary>
/// Floating compatibility host for <see cref="GateLevelSchematicView"/>.
/// The main workspace uses dock documents; standalone callers can still open
/// a regular window without duplicating schematic state or layout ownership.
/// </summary>
public sealed class GateLevelSchematicWindow : Window
{
    private readonly GateLevelSchematicView _view;

    public GateLevelSchematicWindow(GateNetlist netlist)
        : this(netlist, new SchematicConfiguration())
    {
    }

    public GateLevelSchematicWindow(
        GateNetlist netlist,
        RoutingQuality routingQuality,
        bool autoDowngradeLargeGraphs)
        : this(
            netlist,
            new SchematicConfiguration(
                RoutingQuality: routingQuality,
                AutoDowngradeLargeGraphs: autoDowngradeLargeGraphs))
    {
    }

    public GateLevelSchematicWindow(
        GateNetlist netlist,
        SchematicConfiguration schematicSettings)
    {
        Title = $"Gate-Level - {netlist.TopModule}";
        Width = 1200;
        Height = 760;
        Background = SolidColorBrush.Parse("#0e141c");
        _view = new GateLevelSchematicView(
            netlist,
            schematicSettings);
        _view.ScopeTitleChanged += OnScopeTitleChanged;
        Content = _view;
    }

    protected override async void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _view.ScopeTitleChanged -= OnScopeTitleChanged;
        await _view.DisposeAsync();
    }

    private void OnScopeTitleChanged(object? sender, string title)
    {
        Title = title;
    }
}
