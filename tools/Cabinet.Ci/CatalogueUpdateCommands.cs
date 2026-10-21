using System.Net;
using System.Text.Json;

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

    public static int Validate(string baseDirectory, string idsJsonFile)
    {
        try
        {
            var repository = Scenarios.Repo;
            using var client = CreateClient();
            var service = new CatalogueUpdateService(client, repository);
            var selections = service.Validate(baseDirectory, ReadIds(idsJsonFile));
            Console.WriteLine(JsonSerializer.Serialize(selections,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            return 0;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Console.Error.WriteLine($"catalogue update validation failed: {exception.Message}");
            return 1;
        }
    }

    public static int Release(string baseDirectory, string idsJsonFile, string dateText)
    {
        try
        {
            if (!DateOnly.TryParseExact(dateText, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var date)
                || date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) != dateText)
            {
                throw new InvalidDataException("release date must be canonical ISO format");
            }

            using var client = CreateClient();
            var service = new CatalogueUpdateService(client, Scenarios.Repo);
            var ids = ReadIds(idsJsonFile);
            service.GenerateRelease(baseDirectory, ids, date);
            Console.WriteLine($"Generated Cabinet release notes for catalogue updates: {string.Join(", ", ids)}");
            return 0;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Console.Error.WriteLine($"catalogue update release generation failed: {exception.Message}");
            return 1;
        }
    }

    private static string[] ReadIds(string path) =>
        JsonSerializer.Deserialize<string[]>(File.ReadAllText(path))
        ?? throw new InvalidDataException("catalogue update IDs must be a nonempty JSON array");

    private static HttpClient CreateClient() => new(new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        AutomaticDecompression = DecompressionMethods.All
    });
}
