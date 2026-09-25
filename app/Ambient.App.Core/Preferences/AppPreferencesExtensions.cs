namespace Ambient.App.Core.Preferences;

public static class AppPreferencesExtensions
{
    /// <summary>Applies a change and saves. Without preferences (a test) nothing happens.</summary>
    public static void Update(this AppPreferences? preferences, Action<AppPreferences> change)
    {
        if (preferences is null)
        {
            return;
        }

        change(preferences);
        preferences.Save();
    }
}
