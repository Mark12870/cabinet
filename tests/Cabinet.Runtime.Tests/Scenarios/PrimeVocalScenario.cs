namespace Cabinet.Runtime.Tests.Scenarios;

public sealed class PrimeVocalScenario(PrimeVocalScenario.Installed installed)
    : IClassFixture<PrimeVocalScenario.Installed>
{
    private const string Id = "prime-vocal";

    private const string Effect = "VST3: primeVocal";

    private static readonly Dictionary<string, string> Bridges = new()
    {
        ["VST3"] = "primevocal.vst3",
    };

    public static TheoryData<string> Formats => InstalledEntry.Formats(Id);

    [Theory]
    [MemberData(nameof(Formats))]
    public async Task CleansAVoiceTakeThroughAra(string format)
    {
        Assert.True(
            Bridges.ContainsKey(format),
            $"{Id} declares {format}, which this scenario does not say how to find");
        var bridge = installed.Harness.Plugin(format, Bridges[format]);

        var take = await installed.Harness.RenderAsTake(bridge, Effect, installed.Display);

        Assert.InRange(take.NoiseReduction, 10, 100);
        Assert.InRange(take.VoiceChange, -6, 6);
        Assert.InRange(take.Difference, -43, 0);
    }

    public sealed class Installed() : SonibleEntry(Id, "primevocal");
}
