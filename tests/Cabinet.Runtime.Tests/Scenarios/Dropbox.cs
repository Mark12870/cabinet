using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Web;

namespace Cabinet.Runtime.Tests.Scenarios;

internal static class Dropbox
{
    private const string Listing = "https://www.dropbox.com/list_shared_link_folder_entries";

    public static async Task<string> Download(string path, string sha256, string directory)
    {
        var shared = new Uri(Credentials.Read()["DROPBOX"]);
        var cookies = new CookieContainer();
        using var client = new HttpClient(new HttpClientHandler { CookieContainer = cookies });
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (X11; Linux x86_64)");
        await client.GetStringAsync(shared);
        var token = cookies.GetCookies(shared)["t"]!.Value;
        var rlkey = HttpUtility.ParseQueryString(shared.Query)["rlkey"]!;

        var names = path.Split('/');
        var folder = shared;
        var entry = default(JsonElement);
        for (var depth = 0; depth < names.Length; depth++)
        {
            var name = names[depth];
            var within = string.Concat(names.Take(depth).Select(part => "/" + part));
            var entries = await Entries(client, token, folder, within, rlkey);
            var seen = entries.Select(found => found.GetProperty("filename").GetString()).ToList();
            entry = entries.FirstOrDefault(found => found.GetProperty("filename").GetString() == name);
            Assert.True(
                entry.ValueKind == JsonValueKind.Object,
                $"the shared Dropbox folder holds no {name} in /{within.TrimStart('/')}, only: {string.Join(", ", seen)}");
            folder = new Uri(entry.GetProperty("href").GetString()!);
        }

        var file = new UriBuilder(folder);
        var query = HttpUtility.ParseQueryString(file.Query);
        query.Remove("dl");
        query["raw"] = "1";
        file.Query = query.ToString();

        var installer = Path.Combine(directory, names[^1]);
        await Downloads.Fetch(client, file.Uri.AbsoluteUri, installer);
        await using var written = File.OpenRead(installer);
        Assert.Equal(sha256, Convert.ToHexStringLower(await SHA256.HashDataAsync(written)));
        return installer;
    }

    private static async Task<JsonElement[]> Entries(
        HttpClient client, string token, Uri folder, string within, string rlkey)
    {
        var segments = folder.AbsolutePath.Trim('/').Split('/');
        var request = new Dictionary<string, string>
        {
            ["is_xhr"] = "true",
            ["t"] = token,
            ["link_key"] = segments[2],
            ["secure_hash"] = segments[3],
            ["link_type"] = "c",
            ["sub_path"] = within,
            ["rlkey"] = rlkey,
        };
        var entries = new List<JsonElement>();
        while (true)
        {
            using var response = await client.PostAsync(Listing, new FormUrlEncodedContent(request));
            Assert.True(response.IsSuccessStatusCode, $"Dropbox would not list /{within.TrimStart('/')} of the shared folder");
            using var listing = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var page = listing.RootElement;
            entries.AddRange(page.GetProperty("entries").EnumerateArray().Select(found => found.Clone()));
            if (!MoreAfter(page, out var voucher))
            {
                return [.. entries];
            }

            request["voucher"] = voucher;
        }
    }

    private static bool MoreAfter(JsonElement page, out string voucher)
    {
        voucher = page.TryGetProperty("next_request_voucher", out var next) ? next.GetString() ?? "" : "";
        return page.TryGetProperty("has_more_entries", out var more) && more.GetBoolean() && voucher.Length > 0;
    }
}
