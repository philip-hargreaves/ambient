using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ambient.App.Core.Metrics;
using Ambient.App.Core.Ports;
using Ambient.App.Platform;

namespace Ambient.App.Composition;

/// <summary>The WinUI and Win32 adapters behind the Core ports.</summary>
internal static class PlatformServices
{
    public static IServiceCollection AddPlatform(this IServiceCollection services, AppPaths paths)
    {
        services.AddLogging(logging => logging.AddProvider(new FileLoggerProvider(paths.ShellLog)));
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IUiDispatcher, UiDispatcher>();
        services.AddSingleton<WindowAccessor>();
        services.AddSingleton<FocusReturn>();
        services.AddSingleton<IClipboard, WinUiClipboard>();
        services.AddSingleton<IFilePicker, WinUiFilePicker>();
        services.AddSingleton<ILauncher, WinUiLauncher>();
        services.AddSingleton<IThemeService, WinUiThemeService>();
        services.AddSingleton<IDialogService, WinUiDialogService>();
        services.AddSingleton<IMachineInfoProvider, WmiMachineInfoProvider>();
        services.AddSingleton<IProcessMetrics, ProcessMetrics>();
        return services;
    }
}
