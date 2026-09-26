using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ClinicAVT.App.Core.Ports;
using ClinicAVT.App.Platform;

namespace ClinicAVT.App.Composition;

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
        services.AddSingleton<IAppInfo, AppInfo>();
        services.AddSingleton<IProcessMetrics, ProcessMetrics>();
        return services;
    }
}
