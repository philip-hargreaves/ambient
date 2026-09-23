using Microsoft.Extensions.DependencyInjection;
using Ambient.App.Core.Features.Appraisal;
using Ambient.App.Core.Features.Consultation;
using Ambient.App.Core.Features.Demo;
using Ambient.App.Core.Features.Documents;
using Ambient.App.Core.Features.Guidance;
using Ambient.App.Core.Features.Sessions;
using Ambient.App.Core.Features.Settings;
using Ambient.App.Core.Hosting;
using Ambient.App.Core.Shell;

namespace Ambient.App.Core.Composition;

/// <summary>
/// The view models, one instance each, over whatever ports the host registers. Kept here so
/// a test can build the same graph over fakes and prove every constructor still resolves.
/// </summary>
public static class CoreServices
{
    public static IServiceCollection AddCoreViewModels(this IServiceCollection services)
    {
        services.AddSingleton<LiveSessionState>();
        services.AddSingleton<ISessionState>(sp => sp.GetRequiredService<LiveSessionState>());
        services.AddSingleton<TranscriptViewModel>();
        services.AddSingleton<NoteViewModel>();
        services.AddSingleton<DocumentExportViewModel>();
        services.AddSingleton<GuidanceViewModel>();
        services.AddSingleton<PageViewModel>();
        services.AddSingleton<MicViewModel>();
        services.AddSingleton<ConsultationViewModel>();
        services.AddSingleton<SessionControlsViewModel>();
        services.AddSingleton<ShellViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<VoiceViewModel>();
        services.AddSingleton<SessionsViewModel>();
        services.AddSingleton<AppraisalsViewModel>();
        services.AddSingleton<DemoTrayViewModel>();
        return services;
    }
}
