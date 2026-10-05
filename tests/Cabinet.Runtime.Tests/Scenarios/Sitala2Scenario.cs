namespace Cabinet.Runtime.Tests.Scenarios;

public sealed class Sitala2Scenario(Sitala2Scenario.Installed installed)
    : IClassFixture<Sitala2Scenario.Installed>
{
    private const string Id = "sitala-2";

    private const string EmailField = "430 191\tcabinet test";

    private static readonly Dictionary<string, string> Bridges = new()
    {
        ["VST3"] = "Sitala.vst3",
    };

    public static TheoryData<string> Formats => InstalledEntry.Formats(Id);

    [Theory]
    [MemberData(nameof(Formats))]
    public async Task OpensItsActivationPanelInAPrefixOfItsOwn(string format)
    {
        Assert.True(
            Bridges.ContainsKey(format),
            $"{Id} declares {format}, which this scenario does not say how to find");
        var bridge = installed.Harness.Plugin(format, Bridges[format]);

        var editor = installed.Harness.VerifyEditor(bridge, controlsAreParameters: false, type: EmailField);
        Assert.True(
            editor.Wine != "none" && editor.Wine == editor.Told,
            $"Wine places the editor at {editor.Wine} but was told {editor.Told}, so clicks land that far away");
        var audio = await installed.Harness.Render(bridge, "", installed.Display);

        Assert.True(audio.Parameters > 0, $"{installed.Entry.Name} exposed no parameters");
        Assert.Equal("sitala-2", installed.Entry.Prefix);
    }

    public sealed class Installed() : InstalledEntry(Id);
}
