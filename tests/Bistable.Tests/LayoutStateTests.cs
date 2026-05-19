using System.Text.Json;
using System.Text.Json.Serialization;
using Bistable.App.Services;
using Bistable.App.ViewModels;

namespace Bistable.Tests;

public sealed class LayoutStateTests
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    [Fact]
    public void DeserializesDockZonesFromLayoutJson()
    {
        const string json = """
        {
          "waveformZoom": 1.5,
          "waveformOffset": 24,
          "leftDockWidth": 280,
          "rightDockWidth": 340,
          "bottomDockHeight": 320,
          "projectDockZone": "right",
          "waveformDockZone": "bottom"
        }
        """;

        LayoutState? state = JsonSerializer.Deserialize<LayoutState>(json, JsonOptions);

        Assert.NotNull(state);
        Assert.Equal(1.5, state.WaveformZoom);
        Assert.Equal(24, state.WaveformOffset);
        Assert.Equal(280, state.LeftDockWidth);
        Assert.Equal(340, state.RightDockWidth);
        Assert.Equal(320, state.BottomDockHeight);
        Assert.Equal(DockZone.Right, state.ProjectDockZone);
        Assert.Equal(DockZone.Bottom, state.WaveformDockZone);
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        JsonSerializerOptions options = new(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}
