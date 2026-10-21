using System.Net;

namespace Cabinet.Ci;

internal static class CatalogueUpdates
{
    public static async Task<int> Discover(string outputDirectory, bool createCandidates = true)
    {
        try
        {
            var repository = Scenarios.Repo;
            using var client = CreateClient();
            var service = new CatalogueUpdateService(client, repository, Console.Error.WriteLine,
                Environment.GetEnvironmentVariable("GITHUB_TOKEN"));
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(15));
            await service.Discover(outputDirectory, timeout.Token, createCandidates);
            return 0;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Console.Error.WriteLine($"catalogue update discovery failed: {exception.Message}");
            return 1;
        }
        finally
        {
            var report = Path.Combine(outputDirectory, "coverage.md");
            if (Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY") is { Length: > 0 } summary && File.Exists(report))
            {
                File.AppendAllText(summary, File.ReadAllText(report));
            }
        }
    }

    public static int Coverage(string outputDirectory)
    {
        try
        {
            var repository = Scenarios.Repo;
            CatalogueUpdateCoverage.Write(outputDirectory, CatalogueUpdateCoverage.Read(repository));
            return 0;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Console.Error.WriteLine($"catalogue update coverage failed: {exception.Message}");
            return 1;
        }
    }

    public static int Validate(string baseDirectory, string id)
    {
        try
        {
            var repository = Scenarios.Repo;
            using var client = CreateClient();
            var service = new CatalogueUpdateService(client, repository);
            Console.WriteLine(service.Validate(baseDirectory, id));
            return 0;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Console.Error.WriteLine($"catalogue update validation failed: {exception.Message}");
            return 1;
        }
    }

    private static HttpClient CreateClient() => new(new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        AutomaticDecompression = DecompressionMethods.All
    });
}
