namespace Cabinet.Runtime.Tests.Scenarios;

public sealed class PowairScenario(PowairScenario.Installed installed)
    : IClassFixture<PowairScenario.Installed>
{
    private const string Id = "powair";

    private static readonly Dictionary<string, string> Bridges = new()
    {
        ["VST3"] = "POWAIR.vst3",
        ["VST2"] = "POWAIR.so",
    };

    public static TheoryData<string> Formats => InstalledEntry.Formats(Id);

    [Theory]
    [MemberData(nameof(Formats))]
    public void InstallsAndBridgesPowair(string format)
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
