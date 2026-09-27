namespace Cabinet.Runtime.Tests.Scenarios;

public sealed class HelixNativeScenario(HelixNativeScenario.Installed installed)
    : IClassFixture<HelixNativeScenario.Installed>
{
    private const string Id = "helix-native";

    private const string SignInThenStartTrial = "516 457 516 424";

    private const int SetlistLoaded = 15;

    private static readonly Dictionary<string, string> Bridges = new()
    {
        ["VST3"] = "Line 6/Helix Native (x64).vst3",
    };

    public static TheoryData<string> Formats => InstalledEntry.Formats(Id);

    [Theory]
    [MemberData(nameof(Formats))]
    public async Task SignsInStartsItsTrialAndShapesAudio(string format)
    {
        Assert.True(
            Bridges.ContainsKey(format),
            $"{Id} declares {format}, which this scenario does not say how to find");
        var bridge = installed.Harness.Plugin(format, Bridges[format]);

        var editor = installed.Harness.VerifyEditor(
            bridge, controlsAreParameters: false, type: Account(), click: SignInThenStartTrial, still: SetlistLoaded);
        Assert.True(
            editor.Wine != "none" && editor.Wine == editor.Told,
            $"Wine places the editor at {editor.Wine} but was told {editor.Told}, so clicks land that far away");
        var audio = await installed.Harness.Render(bridge, "", installed.Display);

        Assert.True(audio.Parameters > 0, $"{installed.Entry.Name} exposed no parameters");
        Assert.InRange(audio.Before, 0, 0.0001);
        Assert.InRange(audio.Tail, 0.00005, 1);
        Assert.InRange(audio.Peak, 0.024, 1);
    }

    private static string Account()
    {
        var credentials = Credentials.Read();
        return $"516 378\t{credentials["EMAIL"]}\n516 418\t{credentials["PASSWORD"]}";
    }

    public sealed class Installed() : InstalledEntry(Id)
    {
        private protected override async Task Install(Display display) =>
            await Harness.InstallFrom(await Line6.Download("Helix Native", Entry.Version!, Harness.Home), display);
    }
}
