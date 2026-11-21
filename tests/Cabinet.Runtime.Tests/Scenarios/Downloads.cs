using System.Net;

namespace Cabinet.Runtime.Tests.Scenarios;

internal static class Downloads
{
    private const int Attempts = 3;

    private static readonly TimeSpan Stall = TimeSpan.FromSeconds(60);

    private static readonly TimeSpan Backoff = TimeSpan.FromSeconds(20);

    public static async Task Fetch(HttpClient client, string link, string destination)
    {
        var arrived = false;
        for (var attempt = 0; attempt < Attempts && !arrived; attempt++)
        {
            arrived = await Attempt(client, link, destination);
        }

        if (!arrived)
        {
            throw new VendorUnavailableException($"the download of {Path.GetFileName(destination)} stalled {Attempts} times");
        }
    }

    public static async Task<HttpResponseMessage> Get(HttpClient client, string link)
    {
        for (var attempt = 1; ; attempt++)
        {
            HttpResponseMessage response;
            try
            {
                response = await client.GetAsync(link, HttpCompletionOption.ResponseHeadersRead);
            }
            catch (HttpRequestException) when (attempt < Attempts)
            {
                await Task.Delay(Backoff * attempt);
                continue;
            }

            if (!Throttled(response.StatusCode) || attempt == Attempts)
            {
                return response;
            }

            var pause = Pause(response, attempt);
            response.Dispose();
            await Task.Delay(pause);
        }
    }

    private static bool Throttled(HttpStatusCode status) =>
        status == HttpStatusCode.TooManyRequests || (int)status >= 500;

    private static TimeSpan Pause(HttpResponseMessage response, int attempt) =>
        response.Headers.RetryAfter?.Delta is { } asked && asked <= Backoff * Attempts
            ? asked
            : Backoff * attempt;

    private static async Task<bool> Attempt(HttpClient client, string link, string destination)
    {
        using var response = await Get(client, link);
        response.EnsureSuccessStatusCode();
        await using var body = await response.Content.ReadAsStreamAsync();
        await using var file = File.Create(destination);
        using var stall = new CancellationTokenSource(Stall);
        var buffer = new byte[1 << 20];
        try
        {
            int read;
            while ((read = await body.ReadAsync(buffer, stall.Token)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, read));
                stall.CancelAfter(Stall);
            }

            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
