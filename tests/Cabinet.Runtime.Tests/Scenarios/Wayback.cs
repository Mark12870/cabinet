using System.Globalization;
using System.Net;

namespace Cabinet.Runtime.Tests.Scenarios;

internal static class Wayback
{
    private const string Archive = "https://web.archive.org";

    private static readonly TimeSpan Capturing = TimeSpan.FromMinutes(5);

    private static readonly TimeSpan Fresh = TimeSpan.FromHours(1);

    private static readonly TimeSpan CapturedToday = TimeSpan.FromDays(1);

    private static readonly TimeSpan CapturedThisWeek = TimeSpan.FromDays(7);

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
        var (newest, replay) = await Newest(client, url);
        if (newest is not null && Taken(newest) > DateTime.UtcNow - CapturedToday)
        {
            return newest;
        }

        using var saved = await Downloads.Get(client, $"{Archive}/save/{url}");
        if (saved.StatusCode == HttpStatusCode.TooManyRequests || (int)saved.StatusCode >= 500)
        {
            Assert.True(
                newest is not null && Taken(newest) > DateTime.UtcNow - CapturedThisWeek,
                $"the Wayback Machine could not capture {url}: its save service answered {(int)saved.StatusCode} "
                + $"and its replay {replay}, with no capture from this week");
            return newest;
        }

        Assert.True(
            saved.StatusCode == HttpStatusCode.Found && saved.Headers.Location is not null,
            $"the Wayback Machine did not capture {url}: it answered {(int)saved.StatusCode}");
        var capture = Timestamp(saved.Headers.Location!);
        Assert.InRange(Taken(capture), DateTime.UtcNow - Fresh, DateTime.UtcNow + Fresh);
        return capture;
    }

    private static async Task<(string? Capture, int Status)> Newest(HttpClient client, string url)
    {
        using var replayed = await Downloads.Get(client, $"{Archive}/web/2id_/{url}");
        var capture = replayed.StatusCode == HttpStatusCode.Found && replayed.Headers.Location is { } location
            ? Timestamp(location)
            : null;
        return (capture, (int)replayed.StatusCode);
    }

    private static string Timestamp(Uri location) => location.AbsolutePath.Split('/')[2].Replace("id_", "");

    private static DateTime Taken(string capture) =>
        DateTime.ParseExact(
            capture, "yyyyMMddHHmmss", CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
}
