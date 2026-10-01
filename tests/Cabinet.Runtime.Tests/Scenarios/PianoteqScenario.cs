namespace Cabinet.Runtime.Tests.Scenarios;

public sealed class PianoteqScenario(PianoteqScenario.Installed installed)
    : IClassFixture<PianoteqScenario.Installed>
{
    private const string Id = "pianoteq";

    private const int Note = 60;

    private const string DismissDemoNotice = "762 493";

    private static readonly Dictionary<string, string> Bridges = new()
    {
        ["VST3"] = "Pianoteq 9.vst3",
        ["LV2"] = "https://www.modartt.com/lv2/Pianoteq9",
    };

    private static readonly Dictionary<string, string> Condition = new()
    {
        ["VST3"] = "0 343 781",
        ["LV2"] = "20 343 781",
    };

    public static TheoryData<string> Formats => InstalledEntry.Formats(Id);

    [Theory]
    [MemberData(nameof(Formats))]
    public async Task OpensItsEditorAndPlaysANote(string format)
    {
        Assert.True(
            Bridges.ContainsKey(format),
            $"{Id} declares {format}, which this scenario does not say how to find");
        var bridge = installed.Harness.Plugin(format, Bridges[format]);

        installed.Harness.VerifyEditor(bridge, click: DismissDemoNotice, control: Condition[format]);
        var audio = await installed.Harness.Play(bridge, Note, installed.Display);

        Assert.True(audio.Parameters > 0, $"{installed.Entry.Name} exposed no parameters");
        Assert.InRange(audio.Before, 0, 0.00001);
        Assert.InRange(audio.Tail, 0.00032, 1);
        Assert.InRange(audio.Peak, 0.0345, 1);
    }

    public sealed class Installed() : InstalledEntry(Id)
    {
        private protected override Task Install(Display display) =>
            Harness.Install("https://www.modartt.com/data/download/gen/", display);

        protected override void Settle(string home)
        {
            var settings = Path.Combine(home, ".config", "Modartt");
            var release = string.Concat(Entry.Version!.Split('.').Take(2));
            Directory.CreateDirectory(settings);
            File.WriteAllText(
                Path.Combine(settings, $"Pianoteq{release}.prefs"),
                """
                <?xml version="1.0" encoding="UTF-8"?>

                <PROPERTIES>
                  <VALUE name="welcome-done-demo" val="1"/>
                </PROPERTIES>
                """);
        }
    }
}
