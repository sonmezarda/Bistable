using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bistable.App.Services;

// P2.7-9: minimal user-preferences storage. JSON in
// ~/.bistable/preferences.json (or %APPDATA%/Bistable on Windows). Holds the
// settings that persist across runs but aren't tied to a specific project:
// schematic theme, future toggles, etc. Project-scoped state (Phase 2.7-8) lives
// in `.bistable/viewstate.json` next to the design, not here.
public sealed class UserPreferences
{
    [JsonPropertyName("schematicTheme")]
    public SchematicThemePreset SchematicTheme { get; set; } = SchematicThemePreset.Dark;

    [JsonPropertyName("schematicRouter")]
    public SchematicRoutingEngine SchematicRouter { get; set; } = SchematicRoutingEngine.Elk;

    [JsonPropertyName("liveReloadEnabled")]
    public bool LiveReloadEnabled { get; set; } = true;

    [JsonPropertyName("liveReloadDebounceMs")]
    public int? LiveReloadDebounceMs { get; set; }
}

public sealed class UserPreferencesStore
{
    private readonly string _path;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public UserPreferencesStore() : this(ResolveDefaultPath()) { }

    // Test seam: callers can supply an explicit path to isolate from $HOME.
    public UserPreferencesStore(string path) { _path = path; }

    private static string ResolveDefaultPath()
    {
        string baseDir = OperatingSystem.IsWindows()
            ? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".bistable");
        return Path.Combine(baseDir, "preferences.json");
    }

    public UserPreferences Load()
    {
        try
        {
            if (!File.Exists(_path)) return new UserPreferences();
            string json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<UserPreferences>(json, JsonOptions) ?? new UserPreferences();
        }
        catch
        {
            // Corrupted file shouldn't break the app — fall back to defaults.
            // The next Save will overwrite it.
            return new UserPreferences();
        }
    }

    public void Save(UserPreferences prefs)
    {
        string? dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        string json = JsonSerializer.Serialize(prefs, JsonOptions);
        File.WriteAllText(_path, json);
    }
}
