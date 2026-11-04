namespace Cabinet.Runtime.Tests.Scenarios;

public sealed class DrumLevelerScenario(DrumLevelerScenario.Installed installed)
    : IClassFixture<DrumLevelerScenario.Installed>
{
    private const string Id = "drum-leveler";

    private static readonly Dictionary<string, string> Bridges = new()
    {
        ["VST3"] = "DrumLeveler.vst3",
        ["VST2"] = "DrumLeveler.so",
    };

    public static TheoryData<string> Formats => InstalledEntry.Formats(Id);

    [Theory]
    [MemberData(nameof(Formats))]
    public void InstallsAndBridgesDrumLeveler(string format)
    {
        Assert.True(
            Bridges.ContainsKey(format),
            $"{Id} declares {format}, which this scenario does not say how to find");

        Assert.True(
            installed.Harness.Holds(format, Bridges[format]),
            $"{Id} installed but left no bridged {Bridges[format]} for {format}");
    }

    public sealed class Installed() : InstalledEntry(Id);
}
