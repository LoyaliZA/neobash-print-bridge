namespace Neobash.PrintBridge.Core.Dispatch;

public enum AppearancePreference
{
    Light,
    Graphite,
}

public sealed record DesktopPreferences(string? ActiveDestinationId, AppearancePreference Appearance)
{
    public static DesktopPreferences Default { get; } = new(null, AppearancePreference.Light);
}

public interface IPreferenceStore
{
    DesktopPreferences Load();

    void Save(DesktopPreferences preferences);
}
