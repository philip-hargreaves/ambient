namespace Ambient.App.Core.Hosting;

/// <summary>The one place the build configuration is asked.</summary>
public static class BuildFlags
{
    /// <summary>Developer-only surfaces show in a debug build and never ship.</summary>
    public static bool Debug { get; } =
#if DEBUG
        true;
#else
        false;
#endif
}
