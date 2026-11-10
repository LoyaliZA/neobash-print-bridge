using System.Text.Json;

namespace Neobash.PrintBridge.Core.Dispatch;

public sealed class FilePreferenceStore : IPreferenceStore
{
    private readonly string _path;
    private readonly Lock _gate = new();

    public FilePreferenceStore(string root)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            throw new ArgumentException("La ruta de almacenamiento no es válida.", nameof(root));
        }

        Directory.CreateDirectory(root);
        _path = Path.Combine(Path.GetFullPath(root), "preferences.json");
    }

    public DesktopPreferences Load()
    {
        lock (_gate)
        {
            if (!File.Exists(_path))
            {
                return DesktopPreferences.Default;
            }

            try
            {
                var stored = JsonSerializer.Deserialize<StoredPreferences>(File.ReadAllText(_path), DispatchJson.Options);
                if (stored is null)
                {
                    return DesktopPreferences.Default;
                }

                var appearance = Enum.TryParse<AppearancePreference>(stored.Appearance, ignoreCase: true, out var parsed)
                    ? parsed
                    : AppearancePreference.Light;
                var active = string.IsNullOrWhiteSpace(stored.ActiveDestinationId) ? null : stored.ActiveDestinationId;
                return new DesktopPreferences(active, appearance);
            }
            catch (JsonException)
            {
                return DesktopPreferences.Default;
            }
        }
    }

    public void Save(DesktopPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        lock (_gate)
        {
            var stored = new StoredPreferences
            {
                ActiveDestinationId = preferences.ActiveDestinationId,
                Appearance = preferences.Appearance.ToString(),
            };
            var json = JsonSerializer.Serialize(stored, DispatchJson.Options);
            var temporary = _path + ".tmp";
            File.WriteAllText(temporary, json);
            File.Move(temporary, _path, overwrite: true);
        }
    }

    private sealed class StoredPreferences
    {
        public string? ActiveDestinationId { get; set; }

        public string? Appearance { get; set; }
    }
}
