namespace Cabinet.Runtime.Tests.Scenarios;

public sealed class DecentSamplerScenario(DecentSamplerScenario.Installed installed)
    : IClassFixture<DecentSamplerScenario.Installed>
{
    private const string Id = "decent-sampler";

    private const int Note = 60;

    private static readonly Dictionary<string, string> Bridges = new()
    {
        ["VST3"] = "DecentSampler.vst3",
        ["VST2"] = "DecentSampler.so",
    };

    public static TheoryData<string> Formats => InstalledEntry.Formats(Id);

    [Theory]
    [MemberData(nameof(Formats))]
    public async Task OpensItsEditorAndPlaysASampleItIsGiven(string format)
    {
        Assert.True(
            Bridges.ContainsKey(format),
            $"{Id} declares {format}, which this scenario does not say how to find");
        var bridge = installed.Harness.Plugin(format, Bridges[format]);
        var state = Content.ChunkState(
            Path.Combine(installed.Library, $"{format}.carxs"),
            format,
            bridge.Plugin,
            Content.Juce(
                "<DecentSampler minVersion=\"1.0.0\"><groups><group>"
                + $"<sample path=\"{Content.Escape(installed.Sample)}\" rootNote=\"{Note}\" loNote=\"0\" hiNote=\"127\"/>"
                + "</group></groups></DecentSampler>"));

        installed.Harness.VerifyEditor(bridge, controlsAreParameters: false);
        var audio = await installed.Harness.Play(bridge, Note, installed.Display, state: state);

        Assert.True(audio.Parameters > 0, $"{installed.Entry.Name} exposed no parameters");
        Assert.InRange(audio.Before, 0, 0.00001);
        Assert.InRange(audio.Tail, 0, 1);
        Assert.InRange(audio.Peak, 0.02, 1);
    }

    public sealed class Installed() : InstalledEntry(Id)
    {
        public string Library { get; private set; } = "";

        public string Sample { get; private set; } = "";

        protected override void Settle(string home)
        {
            var settings = Path.Combine(home, ".config", "DecentSampler");
            Directory.CreateDirectory(settings);
            File.WriteAllText(
                Path.Combine(settings, "DecentSampler.xml"),
                """
                <?xml version="1.0" encoding="UTF-8"?>

                <PROPERTIES>
                  <VALUE name="welcomeScreenAlreadyShown" val="1"/>
                </PROPERTIES>
                """);
            Library = Path.Combine(home, "library");
            Sample = Content.Hit(Path.Combine(Library, "hit.wav"));
        }
    }
}
