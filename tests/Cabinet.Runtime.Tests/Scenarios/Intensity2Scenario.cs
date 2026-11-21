namespace Cabinet.Runtime.Tests.Scenarios;

public sealed class Intensity2Scenario(Intensity2Scenario.Installed installed)
    : IClassFixture<Intensity2Scenario.Installed>
{
    private const string Id = "intensity-2";

    private static readonly Dictionary<string, string> Bridges = new()
    {
        ["VST3"] = "INTENSITY 2 VST3.vst3",
    };

    public static TheoryData<string> Formats => InstalledEntry.Formats(Id);

    [Theory]
    [MemberData(nameof(Formats))]
    public void InstallsAndBridgesIntensity2(string format)
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
