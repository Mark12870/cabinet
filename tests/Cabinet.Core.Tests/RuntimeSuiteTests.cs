namespace Cabinet.Core.Tests;

public class RuntimeSuiteTests
{
    private const string Project = "tests/Cabinet.Runtime.Tests";

    private const string Matrix = "CarlaTests.cs";

    private static readonly IReadOnlyList<string> Classes = Directory
        .EnumerateFiles(Repo.Path(Project), "*.cs", SearchOption.AllDirectories)
        .Select(path => Path.GetRelativePath(Repo.Path(Project), path))
        .Where(path => !path.StartsWith("bin", StringComparison.Ordinal)
                       && !path.StartsWith("obj", StringComparison.Ordinal))
        .Where(path => File.ReadAllText(Repo.Path($"{Project}/{path}")).Contains("[Fact", StringComparison.Ordinal)
                       || File.ReadAllText(Repo.Path($"{Project}/{path}")).Contains("[Theory", StringComparison.Ordinal))
        .Order(StringComparer.Ordinal)
        .ToList();

    [Fact]
    public void EveryRuntimeTestIsTheMatrixAPluginScenarioOrAPatchProbe()
    {
        var elsewhere = Classes.Where(path =>
            path != Matrix
            && !(path.StartsWith("Scenarios/", StringComparison.Ordinal)
                 && path.EndsWith("Scenario.cs", StringComparison.Ordinal))
            && !path.StartsWith("Patches/", StringComparison.Ordinal));

        Assert.Empty(elsewhere);
    }

    [Fact]
    public void EveryPatchProbeIsOnePatchesMdReads()
    {
        var patches = Repo.Read("PATCHES.md");

        var unread = Classes
            .Where(path => path.StartsWith("Patches/", StringComparison.Ordinal))
            .Select(path => Path.GetFileNameWithoutExtension(path))
            .Where(name => !patches.Contains($"`{name}`", StringComparison.Ordinal));

        Assert.Empty(unread);
    }
}
