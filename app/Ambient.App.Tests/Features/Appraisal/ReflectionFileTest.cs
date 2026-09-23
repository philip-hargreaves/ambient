using Ambient.App.Core.Features.Appraisal;

namespace Ambient.App.Tests.Features.Appraisal;

public class ReflectionFileTest
{
    [Theory]
    [InlineData("Left elbow bursitis", "reflection - Left elbow bursitis")]
    [InlineData("Knee: swelling / effusion?", "reflection - Knee  swelling   effusion")]
    [InlineData("   ", "reflection")]
    public void TheFileNameCarriesTheTitleWithoutTheCharactersAFileCannotHold(string title, string expected) =>
        Assert.Equal(expected, ReflectionFile.FileName(title));
}
