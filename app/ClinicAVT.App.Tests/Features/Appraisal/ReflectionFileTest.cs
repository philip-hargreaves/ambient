using ClinicAVT.App.Core.Features.Appraisal;

namespace ClinicAVT.App.Tests.Features.Appraisal;

public class ReflectionFileTest
{
    [Fact]
    public void TheFileNameCarriesTheTitleWithoutTheCharactersAFileCannotHold()
    {
        Assert.Equal("reflection - Knee  swelling   effusion", ReflectionFile.FileName("Knee: swelling / effusion?"));
        Assert.Equal("reflection", ReflectionFile.FileName("   "));
    }
}
