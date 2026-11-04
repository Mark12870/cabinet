namespace Cabinet.Runtime.Tests.Scenarios;

public sealed class Radical1Scenario(Radical1Scenario.Installed installed)
    : IClassFixture<Radical1Scenario.Installed>
{
    private const string Id = "radical1";

    private static readonly Dictionary<string, string> Bridges = new()
    {
        ["VST3"] = "Radical1.vst3",
    };

    public static TheoryData<string> Formats => InstalledEntry.Formats(Id);

    [Theory]
    [MemberData(nameof(Formats))]
    public void InstallsAndBridgesRadical1(string format)
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
