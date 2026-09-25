using Microsoft.Extensions.Logging;
using Ambient.App.Core.Hosting;
using Ambient.App.Core.Ports;

namespace Ambient.App.Composition;

/// <summary>Runs a stage's tasks in registration order. One failing logs and the rest still run.</summary>
internal sealed class StartupRunner(IEnumerable<IStartupTask> tasks, ILogger<StartupRunner> logger)
{
    public void Run(StartupStage stage)
    {
        foreach (var task in tasks.Where(t => t.Stage == stage))
        {
            try
            {
                task.Run();
            }
            catch (Exception e)
            {
                logger.StartupTaskFailed(e, task.Name);
            }
        }
    }
}
