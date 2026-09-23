namespace Ambient.App.Tests.Support;

/// <summary>Real-model engines contend for the one GPU, so these run alone.</summary>
[CollectionDefinition("engine", DisableParallelization = true)]
public class EngineSuite
{
}
