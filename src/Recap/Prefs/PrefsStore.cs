using System.Text.Json;

namespace Recap.Prefs;

public sealed class PrefsStore
{
    private readonly string _dir;

    public PrefsStore(string? baseDir = null)
    {
        _dir = baseDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "recap");
        Directory.CreateDirectory(_dir);
    }

    private string PrefsPath => Path.Combine(_dir, "prefs.json");
    private string StatePath => Path.Combine(_dir, "state.json");

    public Preferences LoadPrefs()
        => Load(PrefsPath, RecapJsonContext.Default.Preferences) ?? new Preferences();

    public void SavePrefs(Preferences prefs)
        => File.WriteAllText(PrefsPath,
            JsonSerializer.Serialize(prefs, RecapJsonContext.Default.Preferences));

    public StateFile LoadState()
        => Load(StatePath, RecapJsonContext.Default.StateFile) ?? new StateFile();

    public void SaveState(StateFile state)
        => File.WriteAllText(StatePath,
            JsonSerializer.Serialize(state, RecapJsonContext.Default.StateFile));

    private static T? Load<T>(string path, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo)
        where T : class
    {
        if (!File.Exists(path)) return null;
        try { return JsonSerializer.Deserialize(File.ReadAllText(path), typeInfo); }
        catch (JsonException) { return null; }
    }
}
