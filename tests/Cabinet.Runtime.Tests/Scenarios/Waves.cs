using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Cabinet.Core;

namespace Cabinet.Runtime.Tests.Scenarios;

internal sealed partial class Waves(string userGuid, string timeToken, string version)
{
    private const string Service = "https://register.waves.com/CentralService/";

    private const string Cloud = "00000000";

    private const int CloudType = 0;

    private const int ComputerType = 1;

    private const string Nickname = "Cabinet test";

    private const string Storage = "class ElectronMainEncryptedStorage";

    private static readonly HttpClient Client = new();

    public string UserGuid => userGuid;

    public static Waves Restored(string central, string settings)
    {
        var archive = File.ReadAllBytes(Path.Combine(central, "resources", "app.asar"));
        var at = archive.AsSpan().IndexOf(Encoding.ASCII.GetBytes(Storage));
        Assert.True(at >= 0, "Waves Central no longer keeps its session in ElectronMainEncryptedStorage");
        var key = Key().Match(Encoding.ASCII.GetString(archive, at, Math.Min(1000, archive.Length - at)));
        Assert.True(key.Success, "Waves Central's session storage names no key");

        var stored = JsonNode.Parse(File.ReadAllText(settings))!["SessionData"]!.GetValue<string>().Split(':');
        using var aes = Aes.Create();
        aes.Key = Encoding.UTF8.GetBytes(key.Groups[1].Value);
        var session = JsonNode.Parse(Encoding.UTF8.GetString(
            aes.DecryptCbc(Convert.FromHexString(stored[1]), Convert.FromHexString(stored[0]))))!;
        var version = PortableExecutable.Version(Path.Combine(central, "Waves Central.exe"));
        Assert.True(version is not null, "Waves Central carries no version");

        return new Waves(session["userGuid"]!.GetValue<string>(), session["timeToken"]!.GetValue<string>(), version);
    }

    public async Task Activate(string device)
    {
        var licences = await Licences();
        Assert.True(licences.Count > 0, "the test account holds no Waves licence it can activate");
        var elsewhere = licences.Where(licence => licence.Device != device).ToList();

        if (elsewhere.Count > 0)
        {
            await Move(elsewhere, device, ComputerType);
        }
    }

    public async Task Return(string device)
    {
        var here = (await Licences()).Where(licence => licence.Device == device).ToList();

        if (here.Count > 0)
        {
            await Move(here, Cloud, CloudType);
        }
    }

    private async Task<List<(JsonNode Id, string Device)>> Licences()
    {
        var answer = await Post("License/MyLicenses", Session());

        return
        [
            .. answer["oResult"]!.AsArray().SelectMany(file =>
                (file!["Bundles"]?.AsArray() ?? [])
                .Where(bundle => bundle!["IsActivatable"]?.GetValue<bool>() == true)
                .Select(bundle => (bundle!["LicenseID"]!.DeepClone(), file["Device"]!["DeviceID"]!.ToString()))),
        ];
    }

    private Task Move(List<(JsonNode Id, string Device)> licences, string device, int type)
    {
        var request = Session();
        request["DeviceID"] = device;
        request["DeviceTypeID"] = type;
        request["DeviceNickName"] = Nickname;
        request["Licenses"] = new JsonArray(
        [
            .. licences.Select(licence => (JsonNode)new JsonObject
            {
                ["LicenseID"] = licence.Id.DeepClone(),
                ["DeviceID"] = licence.Device,
            }),
        ]);
        request["IsMovingSticky"] = true;
        return Post("License/Move", request);
    }

    private JsonObject Session() => new()
    {
        ["TimeToken"] = timeToken,
        ["UserGuid"] = userGuid,
        ["ClientAppVersion"] = version,
    };

    private static async Task<JsonNode> Post(string method, JsonObject request)
    {
        using var response = await Client.PostAsync(
            Service + method, new StringContent(request.ToJsonString(), Encoding.UTF8, "application/json"));
        Assert.True(response.IsSuccessStatusCode, $"Waves answered {method} with HTTP {(int)response.StatusCode}");
        var answer = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        Assert.True(answer["IsSuccess"]?.GetValue<bool>() == true, $"Waves refused {method}");
        return answer;
    }

    [GeneratedRegex("this\\.key = \"([^\"]+)\"")]
    private static partial Regex Key();
}
