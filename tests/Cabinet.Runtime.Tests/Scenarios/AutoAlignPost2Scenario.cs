namespace Cabinet.Runtime.Tests.Scenarios;

public sealed class AutoAlignPost2Scenario(AutoAlignPost2Scenario.Installed installed)
    : IClassFixture<AutoAlignPost2Scenario.Installed>
{
    private const string Id = "auto-align-post-2";

    private static readonly Dictionary<string, string> Bridges = new()
    {
        ["VST3"] = "AutoAlignPost.vst3",
    };

    public static TheoryData<string> Formats => InstalledEntry.Formats(Id);

    [Theory]
    [MemberData(nameof(Formats))]
    public void InstallsAndBridgesAutoAlignPost2(string format)
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
