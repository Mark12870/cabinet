namespace Cabinet.Runtime.Tests.Scenarios;

public sealed class Sitala1Scenario(Sitala1Scenario.Installed installed)
    : IClassFixture<Sitala1Scenario.Installed>
{
    private const string Id = "sitala-1";

    private const int Note = 36;

    private static readonly Dictionary<string, string> Bridges = new()
    {
        ["VST2"] = "Sitala.so",
    };

    public static TheoryData<string> Formats => InstalledEntry.Formats(Id);

    [Theory]
    [MemberData(nameof(Formats))]
    public async Task OpensItsEditorAndPlaysANote(string format)
    {
        Assert.True(
            Bridges.ContainsKey(format),
            $"{Id} declares {format}, which this scenario does not say how to find");
        var bridge = installed.Harness.Plugin(format, Bridges[format]);

        var editor = installed.Harness.VerifyEditor(bridge);
        Assert.True(
            editor.Wine != "none" && editor.Wine == editor.Told,
            $"Wine places the editor at {editor.Wine} but was told {editor.Told}, so clicks land that far away");
        var audio = await installed.Harness.Play(bridge, Note, installed.Display);

        Assert.True(audio.Parameters > 0, $"{installed.Entry.Name} exposed no parameters");
        Assert.InRange(audio.Before, 0, 0.00001);
        Assert.InRange(audio.Tail, 0, 1);
        Assert.InRange(audio.Peak, 0.0526, 1);
    }

    public sealed class Installed() : InstalledEntry(Id)
    {
        protected override void Settle(string home)
        {
            var users = Path.Combine(Harness.Prefix, "drive_c", "users");
            foreach (var user in Directory.GetDirectories(users))
            {
                var settings = Path.Combine(user, "AppData", "Roaming", "Sitala");
                Directory.CreateDirectory(settings);
                File.WriteAllText(
                    Path.Combine(settings, "Sitala.settings"),
                    """
                    <?xml version="1.0" encoding="UTF-8"?>

                    <PROPERTIES>
                      <VALUE name="viewedMessages" val="7,8"/>
                      <VALUE name="lastUpdateDialog_yabridge-host.exe" val="4102444800000"/>
                    </PROPERTIES>
                    """);
            }
        }
    }
}
