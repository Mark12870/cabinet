using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Cabinet.Runtime.Tests.Scenarios;

internal static class Vital
{
    private const string Account = "https://account.vital.audio";

    private const string SignIn = "https://identitytoolkit.googleapis.com/v1/accounts:signInWithPassword";

    public static async Task<string> Download(string product, string kind, string version, string directory)
    {
        var credentials = Credentials.Read();
        using var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (X11; Linux x86_64)");

        var config = await client.GetStringAsync($"{Account}/js/firebase.js");
        var key = Regex.Match(config, @"apiKey:\s*""([^""]+)""");
        Assert.True(key.Success, "Vital's sign-in page no longer names its Firebase key");

        using var signed = await client.PostAsJsonAsync($"{SignIn}?key={key.Groups[1].Value}", new
        {
            email = credentials["EMAIL"],
            password = credentials["PASSWORD"],
            returnSecureToken = true,
        });
        Assert.True(signed.IsSuccessStatusCode, "Vital's sign-in refused the test account");
        var token = (await signed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("idToken").GetString();

        using var listed = await client.PostAsJsonAsync($"{Account}/products", new
        {
            idToken = token,
            synthVersion = "9.9.9",
            product = "",
        });
        Assert.True(listed.IsSuccessStatusCode, "Vital would not list the test account's products");
        var link = (await listed.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("products").EnumerateArray()
            .Where(found => found.GetProperty("name").GetString() == product)
            .SelectMany(found => found.GetProperty("downloads").EnumerateArray())
            .Where(found => found.GetProperty("name").GetString()!.Trim() == kind
                && found.GetProperty("version").GetString() == version)
            .Select(found => found.GetProperty("link").GetString())
            .FirstOrDefault();
        Assert.True(link is not null, $"Vital offers no {kind} of {product} {version}");

        var archive = Path.Combine(directory, $"{product} {version}.zip");
        await Downloads.Fetch(client, Account + link, archive);
        return archive;
    }
}
