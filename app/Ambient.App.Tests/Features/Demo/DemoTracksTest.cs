using Ambient.App.Core.Features.Demo;
using Ambient.App.Tests.Support;


namespace Ambient.App.Tests.Features.Demo;

public class DemoTracksTest
{
    [Fact]
    public void ManifestListsOnlyTracksWhoseWavsExistABrokenOneYieldsNoneAndDurationComesFromTheHeader()
    {
        var root = Directory.CreateTempSubdirectory("ambient-demo-test");
        var manifest = Path.Combine(root.FullName, "tracks.json");
        try
        {
            File.WriteAllText(manifest, "not json");
            Assert.Empty(DemoTracks.Parse(manifest));

            var wav = Path.Combine(root.FullName, "elbow.wav");
            File.WriteAllBytes(wav, new byte[44]);
            File.WriteAllText(manifest, """
                {"tracks":[
                  {"name":"Elbow swelling","file":"elbow.wav"},
                  {"name":"Missing","file":"gone.wav"}
                ]}
                """);

            var track = Assert.Single(DemoTracks.Parse(manifest));
            Assert.Equal("Elbow swelling", track.Name);
            Assert.Equal(wav, track.Path);
        }
        finally
        {
            root.Delete(recursive: true);
        }

        var real = SessionContractWav.Write(seconds: 3);
        try
        {
            Assert.Equal(3.0, DemoTracks.DurationSeconds(real), 3);
        }
        finally
        {
            File.Delete(real);
        }

        Assert.Equal(0, DemoTracks.DurationSeconds("C:/does/not/exist.wav"));
    }
}
