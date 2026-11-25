namespace Cabinet.Runtime.Tests.Scenarios;

public sealed class PluginBoutiqueAppScenario(PluginBoutiqueAppScenario.Installed installed)
    : IClassFixture<PluginBoutiqueAppScenario.Installed>
{
    private const string Id = "plugin-boutique-app";

    private const string TapeMix = "Dry/Wet";

    private const string ChorusSwitch = "Magic";

    private const string DeclineUpdate = "340 340";

    private static readonly Dictionary<string, string> Tape = new()
    {
        ["VST3"] = "CHOWTapeModel.vst3",
    };

    private static readonly Dictionary<string, string> Chorus = new()
    {
        ["VST3"] = "MagicSwitch.vst3",
    };

    public static TheoryData<string> Formats => InstalledEntry.Formats(Id);

    [Theory]
    [MemberData(nameof(Formats))]
    public async Task InstallsTheTapeModelItDownloadedAndItSaturates(string format)
    {
        Assert.True(Tape.ContainsKey(format), $"{Id} declares {format}, which this scenario does not say how to find");
        var bridge = installed.Harness.Plugin(format, Tape[format]) with { Label = $"{format}-tape" };

        installed.Harness.VerifyEditor(bridge, click: DeclineUpdate);
        var audio = await installed.Harness.Render(bridge, TapeMix, installed.Display);

        Assert.True(audio.MixChanged, $"CHOW Tape Model's {TapeMix} did not hold fully wet");
        Assert.InRange(audio.Before, 0, 0.00001);
        Assert.InRange(audio.Tail, 0, 1);
        Assert.InRange(audio.Peak, 0.023, 1);
    }

    [Theory]
    [MemberData(nameof(Formats))]
    public async Task InstallsTheChorusItDownloadedAndItPlays(string format)
    {
        Assert.True(Chorus.ContainsKey(format), $"{Id} declares {format}, which this scenario does not say how to find");
        var bridge = installed.Harness.Plugin(format, Chorus[format]) with { Label = $"{format}-chorus" };

        installed.Harness.VerifyEditor(bridge);
        var audio = await installed.Harness.Render(bridge, ChorusSwitch, installed.Display);

        Assert.True(audio.MixChanged, $"Magic Switch's {ChorusSwitch} did not stay switched on");
        Assert.InRange(audio.Before, 0, 0.00001);
        Assert.InRange(audio.Tail, 0, 1);
        Assert.InRange(audio.Peak, 0.05, 1);
    }

    public sealed class Installed() : InstalledEntry(Id)
    {
        private const string Installer = "Plugin Boutique/Plugin Boutique 1.0.0-master-1148.exe";

        private const string InstallerSha256 = "aea05e92ca8571c82b1a2e42a71483ee03a16f3b79af0ecf3138e8675b039c8c";

        private const string Program = "plugin boutique.exe";

        private const string SignInPage = "tests/Cabinet.Runtime.Tests/Scenarios/References/plugin-boutique-sign-in.png";

        private const string CredentialFields = "740,142 1060,252";

        private const string Account = "740,160 1060,250";

        private static readonly TimeSpan Opening = TimeSpan.FromMinutes(3);

        private static readonly TimeSpan Answering = TimeSpan.FromMinutes(1);

        private static readonly TimeSpan Installing = TimeSpan.FromMinutes(8);

        private static readonly TimeSpan Step = TimeSpan.FromSeconds(8);

        private protected override async Task Install(Display display)
        {
            await Harness.InstallFrom(await Dropbox.Download(Installer, InstallerSha256, Harness.Home), display);
            using var manager = Harness.Open(display);
            var window = manager.UntitledWindow(Program, Opening);
            manager.Until(() => manager.Shows(window, SignInPage, CredentialFields), window, "the sign-in page", Opening);
            var credentials = Credentials.Read("EMAIL", "PASSWORD");
            manager.Choose(window, 898, 178);
            manager.Type(window, 898, 178, credentials["EMAIL"]);
            manager.Type(window, 898, 232, credentials["PASSWORD"]);
            manager.HoverClick(window, 898, 280);
            manager.Until(() => !manager.Shows(window, SignInPage, CredentialFields), window, "the sign-in", Answering, Account);
            Thread.Sleep(Step * 2);
            manager.Choose(window, 271, 61);
            Thread.Sleep(Step);
            Download(manager, window, "CHOW Tape Model", Tape["VST3"]);
            Download(manager, window, "Magic Switch", Chorus["VST3"]);
            manager.Capture(window, "downloaded");
            await manager.Close();
        }

        private void Download(Manager manager, string window, string product, string bridged)
        {
            manager.Type(window, 962, 102, product);
            Thread.Sleep(Step);
            manager.Capture(window, $"found-{product}");
            manager.HoverClick(window, 1094, 162);
            manager.Until(
                () => Harness.Holds("VST3", bridged), window, $"{bridged} bridged while the app is open", Installing);
        }
    }
}
