using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Cabinet.Runtime.Tests.Scenarios;

public sealed class CymaticsHubScenario(CymaticsHubScenario.Installed installed)
    : IClassFixture<CymaticsHubScenario.Installed>
{
    private const string Id = "cymatics-hub";

    private const string Mix = "Mix";

    private static readonly Dictionary<string, string> Bridges = new()
    {
        ["VST3"] = "Cymatics Diablo Lite.vst3",
    };

    public static TheoryData<string> Formats => InstalledEntry.Formats(Id);

    [Theory]
    [MemberData(nameof(Formats))]
    public async Task InstallsDiabloLiteThatOpensItsEditorAndClips(string format)
    {
        Assert.True(
            Bridges.ContainsKey(format),
            $"{Id} declares {format}, which this scenario does not say how to find");
        var bridge = installed.Harness.Plugin(format, Bridges[format]);

        var editor = installed.Harness.VerifyEditor(bridge);
        Assert.True(
            editor.Wine != "none" && editor.Wine == editor.Told,
            $"Wine places the editor at {editor.Wine} but was told {editor.Told}, so clicks land that far away");
        var audio = await installed.Harness.Render(bridge, Mix, installed.Display);

        Assert.True(audio.Parameters > 0, "Diablo Lite exposed no parameters");
        Assert.True(audio.MixChanged, $"Diablo Lite's {Mix} did not hold fully wet");
        Assert.InRange(audio.Before, 0, 0.00001);
        Assert.InRange(audio.Peak, 0.028, 1);
    }

    public sealed class Installed() : InstalledEntry(Id)
    {
        private const string Api = "https://license-verification-worker.teamcymatics.workers.dev";

        private const string Client = "shp_2233bc05-7f0c-4a61-b9aa-b43af527745c";

        private const string Scheme = "cymaticshub://";

        private const string References = "tests/Cabinet.Runtime.Tests/Scenarios/References/";

        private const string TourPage = References + "cymatics-tour.png";

        private const string TourHeading = "250,205 670,250";

        private const string PluginsPage = References + "cymatics-plugins.png";

        private const string TypeButton = "826,98 904,125";

        private const string TypeMenu = References + "cymatics-type-menu.png";

        private const string TypeItems = "765,130 903,390";

        private const string FreePage = References + "cymatics-free.png";

        private const string DiabloLite = "466,280 676,330";

        private const string InstalledPage = References + "cymatics-installed.png";

        private const string InstalledBadge = "514,500 628,532";

        private static readonly TimeSpan Opening = TimeSpan.FromMinutes(3);

        private static readonly TimeSpan Answering = TimeSpan.FromMinutes(1);

        private static readonly TimeSpan Downloading = TimeSpan.FromMinutes(10);

        private static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(15);

        private protected override async Task Install(Display display)
        {
            await base.Install(display);
            await Harness.Install(Entry.Url!, display);
            await SignIn(Harness.Artefacts);

            using var manager = Harness.Open(display);
            var window = manager.Window(Entry.Name, Opening);
            manager.Until(() => manager.Shows(window, TourPage, TourHeading), window, "the welcome tour", Opening);
            manager.HoverClick(window, 858, 571);
            manager.Until(() => manager.Shows(window, PluginsPage, TypeButton), window, "the Plugins page", Answering);
            manager.HoverClick(window, 864, 111);
            manager.Until(() => manager.Shows(window, TypeMenu, TypeItems), window, "the Type menu", Answering);
            manager.HoverClick(window, 792, 169);
            manager.Until(() => manager.Shows(window, FreePage, DiabloLite), window, "Diablo Lite among the free plugins", Answering);
            manager.HoverClick(window, 570, 516);
            manager.Until(
                () => Harness.Holds("VST3", Bridges["VST3"]) && manager.Shows(window, InstalledPage, InstalledBadge),
                window, "Diablo Lite installed and bridged", Downloading);
            await manager.Close();
        }

        private async Task SignIn(string evidence)
        {
            var credentials = Credentials.Read("EMAIL_IMAP_HOST", "EMAIL", "EMAIL_IMAP_PASSWORD");
            var codeVerifier = Hex(32);
            var handoffVerifier = Hex(32);
            var hubNonce = Hex(16);
            var state = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
            {
                mode = "browser",
                code_verifier = codeVerifier,
                handoff_challenge = Challenge(handoffVerifier),
                hub_nonce = hubNonce,
            })));
            var callback = $"{Api}/hub/auth/callback";
            var url = "https://account.cymatics.fm/authentication/oauth/authorize" +
                $"?client_id={Client}" +
                $"&redirect_uri={Uri.EscapeDataString(callback)}" +
                "&scope=openid+email+customer-account-api%3Afull" +
                "&response_type=code" +
                $"&code_challenge={Challenge(codeVerifier)}" +
                "&code_challenge_method=S256" +
                $"&state={Uri.EscapeDataString(state)}";
            using var browser = new Browser(evidence);
            browser.Go(url);
            browser.Await(() => browser.Shows("input[type=email]"), Opening, "the Cymatics email page");
            browser.Type("input[type=email]", credentials["EMAIL"]);
            browser.Press("Submit");
            browser.Await(
                () => browser.Shows("input[autocomplete=one-time-code]"),
                Opening,
                "the Cymatics verification page");
            var mailbox = new ImapOtp("Cymatics", evidence, credentials);
            var (code, message) = await mailbox.WaitForCodeAsync(CodeLifetime, Opening, TestContext.Current.CancellationToken);
            browser.Type("input[autocomplete=one-time-code]", code);
            var link = browser.Await(
                () => browser.Requests().FirstOrDefault(request => request.StartsWith(Scheme, StringComparison.Ordinal)),
                Opening,
                "the link back to Cymatics Hub");
            var handoff = Query(link, "handoff");
            using var client = new HttpClient { BaseAddress = new Uri(Api) };
            using var response = await client.PostAsJsonAsync(
                "/hub/auth/redeem",
                new { handoff, handoff_verifier = handoffVerifier, hub_nonce = hubNonce },
                TestContext.Current.CancellationToken);
            response.EnsureSuccessStatusCode();
            var auth = JsonNode.Parse(await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken))!.AsObject();
            Assert.True(auth["success"]?.GetValue<bool>(), "Cymatics rejected the browser sign-in handoff");
            Save(auth);
            await mailbox.DeleteAsync(message, TestContext.Current.CancellationToken);
        }

        private void Save(JsonObject auth)
        {
            var user = Directory.GetDirectories(Path.Combine(Harness.Prefix, "drive_c", "users"))
                .Single(path => Directory.Exists(Path.Combine(path, "AppData")) && Path.GetFileName(path) != "Public");
            var data = Path.Combine(user, "AppData", "Roaming", "Cymatics", "Cymatics Hub");
            Directory.CreateDirectory(data);
            var credentials = JsonSerializer.Serialize(new
            {
                email = auth["email"]!.GetValue<string>(),
                password = auth["refresh_token"]!.GetValue<string>(),
            });
            File.WriteAllText(Path.Combine(data, ".credentials"), Convert.ToBase64String(Encoding.UTF8.GetBytes(credentials)));
            if (auth["license"] is { } licence)
            {
                File.WriteAllText(Path.Combine(data, "license.dat"), licence.ToJsonString());
            }
        }

        private static string Hex(int bytes) => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(bytes));

        private static string Challenge(string verifier) => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(verifier)))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

        private static string Query(string url, string name) => Uri.UnescapeDataString(
            new Uri(url).Query.TrimStart('?').Split('&')
                .Select(part => part.Split('=', 2))
                .Single(part => part[0] == name)[1]);
    }
}
