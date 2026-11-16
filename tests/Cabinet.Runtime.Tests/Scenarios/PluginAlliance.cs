using System.Text.Json;
using System.Text.Json.Nodes;

namespace Cabinet.Runtime.Tests.Scenarios;

internal static class PluginAlliance
{
    private const string Shop = "https://www.plugin-alliance.com";

    private const int Machines = 3;

    private static readonly TimeSpan Patience = TimeSpan.FromMinutes(1);

    public static void RetireStaleMachines(
        IReadOnlyDictionary<string, string> credentials, string host, string evidence)
    {
        Directory.CreateDirectory(evidence);
        using var browser = new Browser(evidence);
        browser.Go($"{Shop}/pages/my-devices");
        browser.Await(() => browser.Shows("#username"), Patience, "the Plugin Alliance log-in");
        browser.Type("#username", credentials["EMAIL"]);
        browser.Type("#password", credentials["PASSWORD"]);
        browser.Script("document.querySelector('form button[type=submit][name=action]:not([aria-label])').click()");
        var owner = browser.Await(
            () => browser.Script("return window.accountManager?.options?.auth0Id ?? window.accountManagerConfig?.auth0Id ?? null")?.GetValue<string>(),
            Patience,
            "the Plugin Alliance devices page");

        var (names, licences) = Account(browser);
        List<long> stale = licences.All(licence => licence.Count < Machines)
            ? []
            : licences.SelectMany(licence => licence).Distinct()
                .Where(machine => string.Equals(names.GetValueOrDefault(machine), host, StringComparison.OrdinalIgnoreCase))
                .OrderDescending()
                .Skip(1)
                .ToList();

        foreach (var machine in stale)
        {
            Fetch(browser, "/apps/paauth/app/all-deactivation", new JsonObject { ["auth0_id"] = owner, ["customer_machine_id"] = machine });
        }

        Assert.DoesNotContain(Account(browser).Licences.SelectMany(licence => licence), stale.Contains);

        File.WriteAllLines(Path.Combine(evidence, "retired.txt"), stale.Select(machine => machine.ToString(System.Globalization.CultureInfo.InvariantCulture)));
    }

    private static (Dictionary<long, string> Names, List<List<long>> Licences) Account(Browser browser)
    {
        var account = Fetch(browser, "/apps/paauth/app/customer-data", null)?["account"];
        Assert.True(
            account?["machines"] is JsonArray && account["licenses"] is JsonArray,
            "Plugin Alliance's customer data no longer lists the account's machines and licences");
        return (
            account["machines"]!.AsArray().ToDictionary(machine => Id(machine!["id"]), machine => machine!["name"]!.ToString()),
            [.. account["licenses"]!.AsArray()
                .Where(licence => licence!["license_type"]?["name"]?.ToString() == "full")
                .Select(licence => licence!["machines"]!.AsArray().Select(machine => Id(machine!["id"])).ToList())]);
    }

    private static long Id(JsonNode? id) =>
        long.Parse(id!.ToString(), System.Globalization.CultureInfo.InvariantCulture);

    private static JsonNode? Fetch(Browser browser, string path, JsonObject? body)
    {
        var request = body is null
            ? "{}"
            : $"{{method: 'POST', headers: {{'Content-Type': 'application/json'}}, body: {JsonSerializer.Serialize(body.ToJsonString())}}}";
        var answer = browser.Script(
            $"return fetch({JsonSerializer.Serialize(path)}, {request}).then(async r => ({{ok: r.ok, status: r.status, text: await r.text()}}))")!;
        Assert.True(answer["ok"]!.GetValue<bool>(), $"Plugin Alliance answered {path} with {answer["status"]}");
        var text = answer["text"]!.GetValue<string>();
        return text.TrimStart().StartsWith('{') ? JsonNode.Parse(text) : null;
    }
}
