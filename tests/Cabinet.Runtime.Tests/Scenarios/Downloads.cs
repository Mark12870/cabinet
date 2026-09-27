namespace Cabinet.Runtime.Tests.Scenarios;

internal static class Downloads
{
    private const int Attempts = 3;

    private static readonly TimeSpan Stall = TimeSpan.FromSeconds(60);

    public static async Task Fetch(HttpClient client, string link, string destination)
    {
        var arrived = false;
        for (var attempt = 0; attempt < Attempts && !arrived; attempt++)
        {
            arrived = await Attempt(client, link, destination);
        }

        Assert.True(arrived, $"the download of {Path.GetFileName(destination)} stalled {Attempts} times");
    }

    private static async Task<bool> Attempt(HttpClient client, string link, string destination)
    {
        using var response = await client.GetAsync(link, HttpCompletionOption.ResponseHeadersRead);
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
