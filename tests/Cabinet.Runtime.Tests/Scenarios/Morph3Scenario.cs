namespace Cabinet.Runtime.Tests.Scenarios;

public sealed class Morph3Scenario(Morph3Scenario.Installed installed)
    : IClassFixture<Morph3Scenario.Installed>
{
    private const string Id = "morph-3";

    private static readonly Dictionary<string, string> Bridges = new()
    {
        ["VST3"] = "MORPH 3 VST3.vst3",
    };

    public static TheoryData<string> Formats => InstalledEntry.Formats(Id);

    [Theory]
    [MemberData(nameof(Formats))]
    public void InstallsAndBridgesMorph3(string format)
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
