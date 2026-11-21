namespace Cabinet.Runtime.Tests.Scenarios;

public sealed class Soundtoys5Scenario(Soundtoys5Scenario.Installed installed)
    : IClassFixture<Soundtoys5Scenario.Installed>
{
    private const string Id = "soundtoys-5";

    private static readonly Dictionary<string, string> Bridges = new()
    {
        ["VST3"] = "Soundtoys/EchoBoy.vst3",
    };

    public static TheoryData<string> Formats => InstalledEntry.Formats(Id);

    [Theory]
    [MemberData(nameof(Formats))]
    public void InstallsAndBridgesSoundtoys(string format)
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
