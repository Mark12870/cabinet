using Cabinet.Core;

namespace Cabinet.Core.Tests;

public class WorkflowTests
{
    private static readonly string[] Ci = Repo.Lines(".github/workflows/ci.yml");
    private static readonly string Driver = Repo.Read("scripts/runtime-ci.sh");
    private static readonly string[] Redistributable = ["GPL-3.0", "LGPL-3.0"];

    [Fact]
    public void ARuntimeFailureHoldsTheRelease()
    {
        var publish = Job("publish");

        Assert.Contains(publish, line => Names(line, "needs") && line.Contains("runtime"));
        Assert.Contains(publish, line => line.Contains("needs.runtime.result == 'success'"));
    }

    [Fact]
    public void TheRuntimeSuiteRunsOnEveryPushToMainAgainstTheBuiltArtifact()
    {
        var runtime = Job("runtime");

        Assert.Contains(runtime, line => Names(line, "needs") && line.Contains("build"));
        Assert.Contains(runtime, line => line.Contains("github.ref == 'refs/heads/main'"));
        Assert.Contains(runtime, line => line.Contains("name: repo"));
    }

    [Theory]
    [InlineData(".github/workflows/ci.yml")]
    [InlineData(".github/workflows/runtime-image.yml")]
    public void AWorkflowReachesTheRuntimeSuiteOnlyThroughItsOwnScript(string workflow)
    {
        var lines = Repo.Lines(workflow);

        Assert.Contains(lines, line => line.Contains("runtime-ci.sh test"));
        Assert.DoesNotContain(lines, line => line.Contains("dotnet test"));
        Assert.DoesNotContain(
            lines,
            line => line.Contains("setup-runtime-tests.sh") && !line.Contains("hashFiles("));
    }

    [Theory]
    [InlineData(".github/workflows/ci.yml")]
    [InlineData(".github/workflows/runtime-image.yml")]
    public void TheImageIsStrippedBeforeItIsPublished(string workflow)
    {
        var lines = Repo.Lines(workflow);
        var stripped = Array.FindIndex(lines, line => line.Contains("runtime-ci.sh clean"));
        var published = Array.FindIndex(lines, line => line.Contains("docker push"));

        Assert.True(stripped >= 0, $"{workflow} publishes an image it never strips");
        Assert.True(published > stripped, $"{workflow} pushes the image before stripping it");
    }

    [Fact]
    public void NoFixtureAVendorOwnsRidesInThePublishedImage()
    {
        var shipped = Catalogue.Entries().ToDictionary(entry => entry.Id);

        var exposed = Repo.Lines("scripts/setup-runtime-tests.sh")
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("install_entry ", StringComparison.Ordinal))
            .Select(line => shipped[line.Split(' ')[1]])
            .Where(entry => !Redistributable.Contains(entry.Licence))
            .Select(entry => entry.Kind == PluginKind.Windows
                ? $"prefixes/{entry.Prefix}"
                : $"native/{entry.Id}")
            .Where(fixture => !Driver.Contains(fixture, StringComparison.Ordinal));

        Assert.Empty(exposed);
    }

    [Theory]
    [InlineData("scripts/setup-runtime-tests.sh")]
    [InlineData("scripts/runtime-ci.sh")]
    public void OneListOfPackagesServesBothTheToolboxAndTheImage(string script)
    {
        Assert.Contains(Repo.Lines(script), line => line.Contains("runtime-packages.txt"));
    }

    private static bool Names(string line, string key) =>
        line.TrimStart().StartsWith(key + ":", StringComparison.Ordinal);

    private static IReadOnlyList<string> Job(string name)
    {
        var body = Ci
            .SkipWhile(line => line != $"  {name}:")
            .Skip(1)
            .TakeWhile(line => line.Length == 0 || line.StartsWith("   ", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(body);
        return body;
    }
}
