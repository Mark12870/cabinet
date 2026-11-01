using System.Globalization;
using System.Net;

namespace Cabinet.Runtime.Tests.Scenarios;

internal static class Wayback
{
    private const string Archive = "https://web.archive.org";

    private static readonly TimeSpan Capturing = TimeSpan.FromMinutes(5);

    private static readonly TimeSpan Fresh = TimeSpan.FromHours(1);

    public static async Task<string> Download(string url, string directory)
    {
        var capture = await Capture(url);
        var installer = Path.Combine(directory, Path.GetFileName(new Uri(url).AbsolutePath));
        using var client = new HttpClient { Timeout = Capturing };
        await Downloads.Fetch(client, $"{Archive}/web/{capture}id_/{url}", installer);
        return installer;
    }

    private static async Task<string> Capture(string url)
    {
        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = Capturing };
        using var saved = await client.GetAsync($"{Archive}/save/{url}");
        Assert.True(
            saved.StatusCode == HttpStatusCode.Found && saved.Headers.Location is not null,
            $"the Wayback Machine did not capture {url}: it answered {(int)saved.StatusCode}");

        var capture = saved.Headers.Location!.AbsolutePath.Split('/')[2];
        var taken = DateTime.ParseExact(
            capture, "yyyyMMddHHmmss", CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
        Assert.InRange(taken, DateTime.UtcNow - Fresh, DateTime.UtcNow + Fresh);
        return capture;
    }
}
