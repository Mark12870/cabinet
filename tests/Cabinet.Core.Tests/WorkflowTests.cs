using Cabinet.Core;

namespace Cabinet.Core.Tests;

public class WorkflowTests
{
    private static readonly string[] Ci = Repo.Lines(".github/workflows/ci.yml");
    private static readonly string[] Runtime = Repo.Lines(".github/workflows/runtime.yml");
    private static readonly string[] Plugins = Repo.Lines(".github/workflows/plugins.yml");
    private static readonly string Driver = Repo.Read("scripts/runtime-ci.sh");
    private static readonly string[] Redistributable = ["GPL-3.0", "LGPL-3.0"];

    [Fact]
    public void ARuntimeFailureHoldsTheRelease()
    {
        var publish = Job(Ci, "publish");

        Assert.Contains(publish, line => Names(line, "needs") && line.Contains("runtime"));
        Assert.Contains(publish, line => line.Contains("needs.runtime.result == 'success'"));
    }

    [Fact]
    public void TheRuntimeSuiteRunsOnEveryPushToMainAgainstTheBuiltArtifact()
    {
        var caller = Job(Ci, "runtime");
        var runtime = Job(Runtime, "runtime");

        Assert.Contains(caller, line => line.Contains("github.ref == 'refs/heads/main'"));
        Assert.Contains(caller, line => line.Contains("uses: ./.github/workflows/runtime.yml"));
        Assert.Contains(runtime, line => line.Contains("name: repo"));
        Assert.Contains(runtime, line => line.Contains("Wait for the built Cabinet"));
    }

    [Fact]
    public void TheWorkflowReachesTheRuntimeSuiteOnlyThroughItsOwnScript()
    {
        var lines = Directory.EnumerateFiles(Repo.Path(".github/workflows"), "*.yml")
            .SelectMany(File.ReadLines)
            .ToList();

        Assert.Contains(Runtime, line => line.Contains("runtime-ci.sh test"));
        Assert.DoesNotContain(lines, line => line.Contains("dotnet test"));
        Assert.DoesNotContain(
            lines,
            line => line.Contains("setup-runtime-tests.sh") && !line.Contains("hashFiles("));
    }

    [Fact]
    public void TheImageIsStrippedBeforeItIsPublished()
    {
        var stripped = Array.FindIndex(Runtime, line => line.Contains("runtime-ci.sh clean"));
        var published = Array.FindIndex(Runtime, line => line.Contains("docker push"));

        Assert.True(stripped >= 0, "the workflow publishes an image it never strips");
        Assert.True(published > stripped, "the workflow pushes the image before stripping it");
    }

    [Fact]
    public void TheImageNameIsLowercasedRatherThanInterpolated()
    {
        Assert.DoesNotContain(Runtime, line => line.Contains("ghcr.io/${{"));
        Assert.Contains(Runtime, line => line.Contains("IMAGE=ghcr.io/${owner,,}"));
    }

    [Fact]
    public void PluginScenariosRunOnlyOnTheScheduleAndNeverOnAPush()
    {
        const string scenarios = "Cabinet.Runtime.Tests.Scenarios";

        Assert.Contains(Plugins, line => line.Trim() == "suite: scenarios");
        Assert.Contains(Plugins, line => line.Trim() == "schedule:");
        Assert.DoesNotContain(Plugins, line => line.Trim() is "push:" or "pull_request:");
        Assert.DoesNotContain(Ci, line => Names(line, "suite"));
        Assert.Contains($"general) filter='FullyQualifiedName!~{scenarios}.'", Driver, StringComparison.Ordinal);
        Assert.Contains($"scenarios) filter='FullyQualifiedName~{scenarios}.'", Driver, StringComparison.Ordinal);
        Assert.All(
            Directory.EnumerateFiles(Repo.Path("tests/Cabinet.Runtime.Tests/Scenarios"), "*.cs"),
            file => Assert.Contains($"namespace {scenarios};", File.ReadAllText(file), StringComparison.Ordinal));
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

    private static IReadOnlyList<string> Job(string[] workflow, string name)
    {
        var body = workflow
            .SkipWhile(line => line != $"  {name}:")
            .Skip(1)
            .TakeWhile(line => line.Length == 0 || line.StartsWith("   ", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(body);
        return body;
    }
}
