namespace Cabinet.Runtime.Tests.Scenarios;

public sealed class DrumGizmoScenario(DrumGizmoScenario.Installed installed)
    : IClassFixture<DrumGizmoScenario.Installed>
{
    private const string Id = "drumgizmo";

    private const string Uri = "http://drumgizmo.org/lv2";

    private const int Note = 36;

    private static readonly Dictionary<string, string> Bridges = new()
    {
        ["LV2"] = Uri,
    };

    public static TheoryData<string> Formats => InstalledEntry.Formats(Id);

    [Theory]
    [MemberData(nameof(Formats))]
    public async Task OpensItsEditorAndPlaysAKitItIsGiven(string format)
    {
        Assert.True(
            Bridges.ContainsKey(format),
            $"{Id} declares {format}, which this scenario does not say how to find");
        var bridge = installed.Harness.Plugin(format, Bridges[format]);

        installed.Harness.VerifyEditor(bridge, controlsAreParameters: false);
        var audio = await installed.Harness.Play(bridge, Note, installed.Display, state: installed.State);

        Assert.True(audio.Parameters > 0, $"{installed.Entry.Name} exposed no parameters");
        Assert.InRange(audio.Before, 0, 0.00001);
        Assert.InRange(audio.Tail, 0, 1);
        Assert.InRange(audio.Peak, 0.049, 1);
    }

    public sealed class Installed() : InstalledEntry(Id)
    {
        public string State { get; private set; } = "";

        protected override void Settle(string home)
        {
            var kit = Path.Combine(home, "kit");
            Content.Hit(Path.Combine(kit, "Kick", "kick.wav"));
            File.WriteAllText(
                Path.Combine(kit, "Kick", "kick.xml"),
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <instrument version="2.0" name="Kick">
                  <samples>
                    <sample name="Kick-1" power="1">
                      <audiofile channel="Mono" file="kick.wav" filechannel="1"/>
                    </sample>
                  </samples>
                </instrument>
                """);
            var drumkit = Path.Combine(kit, "drumkit.xml");
            File.WriteAllText(
                drumkit,
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <drumkit version="2.0" name="Scenario" samplerate="48000">
                  <channels>
                    <channel name="Mono"/>
                  </channels>
                  <instruments>
                    <instrument name="Kick" file="Kick/kick.xml">
                      <channelmap in="Mono" out="Mono" main="true"/>
                    </instrument>
                  </instruments>
                </drumkit>
                """);
            var midimap = Path.Combine(kit, "midimap.xml");
            File.WriteAllText(
                midimap,
                $"""
                <?xml version="1.0" encoding="UTF-8"?>
                <midimap>
                  <map note="{Note}" instr="Kick"/>
                </midimap>
                """);
            State = Content.Lv2State(
                Path.Combine(kit, "state.carxs"),
                Uri,
                "http://drumgizmo.org/lv2/atom#config",
                $"""
                <config version="1.0">
                  <value name="drumkitfile">{Content.Escape(drumkit)}</value>
                  <value name="midimapfile">{Content.Escape(midimap)}</value>
                </config>
                """);
        }
    }
}
