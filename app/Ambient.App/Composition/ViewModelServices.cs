using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ambient.App.Core.Composition;
using Ambient.App.Core.Features.Demo;
using Ambient.App.Core.Hosting;
using Ambient.App.Core.Metrics;
using Ambient.App.Core.Ports;
using Ambient.App.Core.Preferences;
using Ambient.App.Core.Shell;
using Ambient.App.Platform;
using Ambient.Client;

namespace Ambient.App.Composition;

/// <summary>The preferences, the metrics collector and every view model, one instance each.</summary>
internal static class ViewModelServices
{
    public static IServiceCollection AddViewModels(this IServiceCollection services, AppPaths paths)
    {
        services.AddSingleton(sp => AppPreferences.Load(
            paths.Preferences, sp.GetRequiredService<ILogger<AppPreferences>>()));
        services.AddSingleton(sp => new PerformanceCollector(
            sp.GetRequiredService<IEngineApi>(),
            () => sp.GetRequiredService<AppPreferences>().CollectPerformanceData,
            () => sp.GetRequiredService<IEngineHost>().EnginePid,
            paths.Metrics,
            sp.GetRequiredService<IProcessMetrics>(), PowerStateReader.Read,
            sp.GetRequiredService<ILogger<PerformanceCollector>>()));
        services.AddSingleton(sp => new DemoMode(sp.GetRequiredService<AppPreferences>(), paths.Masters));

        services.AddSingleton(sp => new StatusBarViewModel(
            sp.GetRequiredService<IEngineApi>(), sp.GetRequiredService<IUiDispatcher>(),
            memoryGb: () => sp.GetRequiredService<IProcessMetrics>()
                .WorkingSetGb(EngineLayout.EngineProcess, EngineLayout.NoteHostProcess),
            logger: sp.GetRequiredService<ILogger<StatusBarViewModel>>()));
        services.AddSingleton<CreditsViewModel>();
        services.AddCoreViewModels();
        return services;
    }
}
