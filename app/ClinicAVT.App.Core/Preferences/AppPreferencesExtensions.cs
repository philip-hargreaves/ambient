namespace ClinicAVT.App.Core.Preferences;

public static class AppPreferencesExtensions
{
    /// <summary>
    /// Applies a change and saves. Does nothing without preferences, as in a test.
    /// </summary>
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
