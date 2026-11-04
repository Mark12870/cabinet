namespace Cabinet.Runtime.Tests.Scenarios;

public sealed class SurferEq2Scenario(SurferEq2Scenario.Installed installed)
    : IClassFixture<SurferEq2Scenario.Installed>
{
    private const string Id = "surfereq-2";

    private static readonly Dictionary<string, string> Bridges = new()
    {
        ["VST3"] = "SurferEQ.vst3",
        ["VST2"] = "SurferEQ.so",
    };

    public static TheoryData<string> Formats => InstalledEntry.Formats(Id);

    [Theory]
    [MemberData(nameof(Formats))]
    public void InstallsAndBridgesSurferEq2(string format)
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
