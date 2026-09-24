namespace Cabinet.Runtime.Tests.Scenarios;

public sealed class TalFilter2Scenario(TalFilter2Scenario.Installed installed)
    : IClassFixture<TalFilter2Scenario.Installed>
{
    private const string Id = "tal-filter-2";

    private const string Mix = "Param Depth";

    private static readonly Dictionary<string, string> Bridges = new()
    {
        ["VST3"] = "TAL-Filter-2.vst3",
        ["VST2"] = "libTAL-Filter-2.so",
    };

    public static TheoryData<string> Formats => InstalledEntry.Formats(Id);

    [Theory]
    [MemberData(nameof(Formats))]
    public async Task OpensItsEditorAndFilters(string format)
    {
        Assert.True(
            Bridges.ContainsKey(format),
            $"{Id} declares {format}, which this scenario does not say how to find");
        var bridge = installed.Harness.Plugin(format, Bridges[format]);

        installed.Harness.VerifyEditor(bridge);
        var audio = await installed.Harness.Render(bridge, Mix, installed.Display);

        Assert.True(audio.Parameters > 0, $"{installed.Entry.Name} exposed no parameters");
        Assert.True(audio.MixChanged, $"{installed.Entry.Name}'s {Mix} did not hold fully wet");
        Assert.InRange(audio.Before, 0, 0.00001);
        Assert.InRange(audio.Tail, 0, 1);
        Assert.InRange(audio.Peak, 0.025, 1);
    }

    public sealed class Installed() : InstalledEntry(Id);
}
