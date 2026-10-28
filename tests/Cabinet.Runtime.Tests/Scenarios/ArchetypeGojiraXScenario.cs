namespace Cabinet.Runtime.Tests.Scenarios;

public sealed class ArchetypeGojiraXScenario(ArchetypeGojiraXScenario.Installed installed)
    : IClassFixture<ArchetypeGojiraXScenario.Installed>
{
    private const string Id = "archetype-gojira-x";

    private static readonly Dictionary<string, string> Bridges = new()
    {
        ["VST3"] = "Archetype Gojira X.vst3",
        ["VST2"] = "Archetype Gojira X.so",
    };

    public static TheoryData<string> Formats => InstalledEntry.Formats(Id);

    [Theory]
    [MemberData(nameof(Formats))]
    public void InstallsAndBridgesGojiraX(string format)
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
