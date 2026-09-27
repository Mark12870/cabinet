using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Cabinet.Runtime.Tests.Scenarios;

internal static class Line6
{
    private const string Site = "https://line6.com";

    public static async Task<string> Download(string product, string version, string directory)
    {
        var credentials = Credentials.Read();
        using var client = new HttpClient(new HttpClientHandler { CookieContainer = new CookieContainer() });
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0");

        await client.GetStringAsync($"{Site}/account/login.html");
        var account = await Post(client, "/account/login.html", new()
        {
            ["action"] = "login",
            ["redirect"] = "/account/login.html",
            ["l_email_user"] = credentials["EMAIL"],
            ["l_pass"] = credentials["PASSWORD"],
        });
        Assert.True(
            account.Contains("logout", StringComparison.OrdinalIgnoreCase),
            "Line 6 refused the test account's sign-in");

        var listing = await client.GetStringAsync(
            $"{Site}/software/index.html?name={Uri.EscapeDataString(product)}&os=All&submit_form=set");
        var release = Release(listing, version);
        var accepted = await Post(client, "/software/readeula.html", new()
        {
            ["rid"] = release,
            ["submit"] = "ACCEPT AND DOWNLOAD",
        });
        var link = Regex.Match(accepted, @"href=""(https://line6\.com/getrelease\?[^""]+)""");
        var md5 = Regex.Match(accepted, @"MD5</th>\s*<td[^>]*>([0-9a-f]{32})<");
        Assert.True(link.Success && md5.Success, $"Line 6 gave no download for {product} {version} after its licence");

        var installer = Path.Combine(directory, $"{product} {version}.exe");
        await Downloads.Fetch(client, link.Groups[1].Value, installer);
        await using var written = File.OpenRead(installer);
        Assert.Equal(md5.Groups[1].Value, Convert.ToHexStringLower(await MD5.HashDataAsync(written)));
        return installer;
    }

    private static string Release(string listing, string version)
    {
        var found = Regex.Match(
            listing,
            $@"<b>Version {Regex.Escape(version)}</b>(?:(?!software-results).)*?Compatible OS:</b>\s*Windows"
            + @"(?:(?!software-results).)*?readeula\.html\?rid=(\d+)",
            RegexOptions.Singleline);
        Assert.True(found.Success, $"Line 6 lists no Windows release of version {version}");
        return found.Groups[1].Value;
    }

    private static async Task<string> Post(HttpClient client, string path, Dictionary<string, string> form)
    {
        using var response = await client.PostAsync(Site + path, new FormUrlEncodedContent(form));
        return await response.Content.ReadAsStringAsync();
    }
}
