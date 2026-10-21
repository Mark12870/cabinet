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
    public void FullPluginScenariosRunOnTheScheduleAndCandidatesUseTheirOwnScenario()
    {
        const string scenarios = "Cabinet.Runtime.Tests.Scenarios";

        Assert.Contains(Plugins, line => line.Trim() == "suite: scenarios");
        Assert.Contains(Plugins, line => line.Trim() == "schedule:");
        Assert.DoesNotContain(Plugins, line => line.Trim() is "push:" or "pull_request:");
        Assert.Contains(Job(Ci, "candidate-runtime"), line => line.Trim() == "suite: scenarios");
        Assert.Contains($"general) filter=(--filter-not-namespace {scenarios}", Driver, StringComparison.Ordinal);
        Assert.Contains($"scenarios) filter=(--filter-namespace {scenarios})", Driver, StringComparison.Ordinal);
        Assert.All(
            Directory.EnumerateFiles(Repo.Path("tests/Cabinet.Runtime.Tests/Scenarios"), "*.cs"),
            file => Assert.Contains($"namespace {scenarios};", File.ReadAllText(file), StringComparison.Ordinal));
    }

    [Fact]
    public void OnlyAFailedPluginRunsDiagnosisHoldsTheModelKey()
    {
        var diagnose = Job(Plugins, "diagnose");

        Assert.Contains(diagnose, line => line.Trim() == "if: failure()");
        Assert.Single(Plugins, line => line.Contains("secrets.GROQ_API_KEY", StringComparison.Ordinal));
        Assert.Contains(diagnose, line => line.Contains("secrets.GROQ_API_KEY", StringComparison.Ordinal));
        Assert.DoesNotContain(Runtime, line => line.Contains("GROQ", StringComparison.Ordinal));
        Assert.DoesNotContain("GROQ", Driver, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePluginDiagnosisGoesToTheWorkflowSummaryAndEmailWithoutIssues()
    {
        var diagnose = string.Join('\n', Job(Plugins, "diagnose"));

        Assert.Contains("echo '## AI summary'", diagnose, StringComparison.Ordinal);
        Assert.Contains("cat diagnosis.md >> \"${GITHUB_STEP_SUMMARY}\"", diagnose, StringComparison.Ordinal);
        Assert.Contains("scripts/email-diagnosis.sh diagnosis.md", diagnose, StringComparison.Ordinal);
        Assert.DoesNotContain(Plugins, line => line.Contains("gh issue", StringComparison.Ordinal));
        Assert.DoesNotContain(Plugins, line => line.Trim() == "issues: write");
    }

    [Theory]
    [InlineData("SMTP_HOST", "vars.SMTP_HOST || 'smtp.agentmail.to'")]
    [InlineData("SMTP_PORT", "vars.SMTP_PORT || '465'")]
    [InlineData("SMTP_USERNAME", "secrets.SMTP_USERNAME")]
    [InlineData("SMTP_PASSWORD", "secrets.SMTP_PASSWORD")]
    [InlineData("SMTP_TO", "secrets.SMTP_TO || vars.SMTP_TO")]
    public void OnlyTheDiagnosisEmailStepReceivesMailConfiguration(string name, string value)
    {
        var steps = string.Join('\n', Job(Plugins, "diagnose")).Split("\n      - ", StringSplitOptions.None);

        var email = Assert.Single(steps, step => step.Contains($"{name}:", StringComparison.Ordinal));
        Assert.Contains("name: Email the full diagnosis", email, StringComparison.Ordinal);
        Assert.Contains($"{name}: ${{{{ {value} }}}}", email, StringComparison.Ordinal);
        Assert.DoesNotContain(Runtime, line => line.Contains(name, StringComparison.Ordinal));
        Assert.DoesNotContain(name, Driver, StringComparison.Ordinal);
    }

    [Fact]
    public void PatchProbesRemainLocalAndOutsideTheCiSuites()
    {
        const string probes = "Cabinet.Runtime.Tests.Patches";

        var workflows = Directory.EnumerateFiles(Repo.Path(".github/workflows"), "*.yml")
            .SelectMany(File.ReadLines);

        Assert.False(File.Exists(Repo.Path(".github/workflows/patches.yml")));
        Assert.DoesNotContain(workflows, line => line.Trim() == "suite: patches");
        Assert.DoesNotContain(Runtime, line => line.Contains("CABINET_RUNTIME_PROBES", StringComparison.Ordinal));
        Assert.Contains($"--filter-not-namespace {probes}) ;;", Driver, StringComparison.Ordinal);
        Assert.DoesNotContain($"--filter-namespace {probes}", Driver, StringComparison.Ordinal);
        Assert.All(
            Directory.EnumerateFiles(Repo.Path("tests/Cabinet.Runtime.Tests/Patches"), "*.cs"),
            file => Assert.Contains($"namespace {probes};", File.ReadAllText(file), StringComparison.Ordinal));
    }

    [Fact]
    public void TargetedPluginRunsTakeAClassFilterAndRunAlone()
    {
        var scenarios = Driver.Split("scenarios) filter=", StringSplitOptions.None)[1]
            .Split(";;", StringSplitOptions.None)[0];

        Assert.DoesNotContain(Plugins, line => line.Contains("filter_class", StringComparison.Ordinal));
        Assert.Contains(Runtime, line => line.Contains("--env CABINET_RUNTIME_FILTER_CLASS=", StringComparison.Ordinal));
        Assert.Contains("parallel=(--parallel collections)", scenarios, StringComparison.Ordinal);
        Assert.Contains("filter+=(--filter-class \"$CABINET_RUNTIME_FILTER_CLASS\")", scenarios, StringComparison.Ordinal);
        Assert.Contains("parallel=(--parallel none)", scenarios, StringComparison.Ordinal);
    }

    [Fact]
    public void AutomaticMergingRequiresChecksBuildAndTheCandidateScenario()
    {
        var merge = Job(Ci, "merge-candidate");
        var runtime = Job(Ci, "candidate-runtime");

        Assert.Contains(merge, line => line.Trim() == "needs: [candidate, checks, build, candidate-runtime]");
        Assert.Contains(merge, line => line.Contains("--match-head-commit", StringComparison.Ordinal));
        Assert.Contains(merge, line => line.Contains("\"$current\" != \"$BASE\"", StringComparison.Ordinal));
        Assert.Contains(runtime, line => line.Contains("entry_id: ${{ needs.candidate.outputs.id }}", StringComparison.Ordinal));
        Assert.Contains(Runtime, line => line.Contains("scenario-approve runtime/TestResults/runtime.trx", StringComparison.Ordinal));
        Assert.DoesNotContain(Runtime, line => line.Contains("CATALOGUE_UPDATE_APP_PRIVATE_KEY", StringComparison.Ordinal));
        Assert.Contains(runtime, line => line.Trim() == "packages: read");
    }

    [Fact]
    public void CandidatesAreValidatedUsingTrustedBaseCodeAndOnlyOneCatalogueFile()
    {
        var candidate = Job(Ci, "candidate");

        Assert.Contains(candidate, line => line.Contains("ref: ${{ github.event.pull_request.base.sha || github.sha }}", StringComparison.Ordinal));
        Assert.Contains(candidate, line => line.Contains("github.event.pull_request.user.login == vars.CATALOGUE_UPDATE_BOT", StringComparison.Ordinal));
        Assert.Contains(candidate, line => line.Contains("github.event.pull_request.head.repo.full_name == github.repository", StringComparison.Ordinal));
        Assert.Contains(candidate, line => line.Contains("[ \"${#changed[@]}\" -eq 1 ]", StringComparison.Ordinal));
        Assert.Contains(candidate, line => line.Contains("catalogue-updates validate", StringComparison.Ordinal));
    }

    [Fact]
    public void DiscoveryReportsRunWithoutWriterCredentialsAndSurviveSourceFailures()
    {
        var workflow = Repo.Lines(".github/workflows/catalogue-updates.yml");
        var discover = Job(workflow, "discover");
        var propose = Job(workflow, "propose");

        Assert.DoesNotContain(discover, line => line.Contains("CATALOGUE_UPDATE_APP", StringComparison.Ordinal));
        Assert.DoesNotContain(discover, line => line.Contains("CATALOGUE_UPDATE_BOT", StringComparison.Ordinal));
        Assert.Contains(discover, line => line.Trim() == "if: always()");
        Assert.Contains(discover, line => line.Contains("exit \"$status\"", StringComparison.Ordinal));
        Assert.Contains(propose, line => line.Trim() == "!cancelled()");
        Assert.Contains(propose, line => line.Contains("vars.CATALOGUE_UPDATE_APP_ID != ''", StringComparison.Ordinal));
        Assert.Contains(propose, line => line.Contains("vars.CATALOGUE_UPDATE_BOT != ''", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("EMAIL_IMAP_HOST", "vars.EMAIL_IMAP_HOST")]
    [InlineData("EMAIL_IMAP_PASSWORD", "secrets.EMAIL_IMAP_PASSWORD")]
    public void TheRuntimeMailboxSettingsReachTheScenarioCredentialsFile(string name, string source)
    {
        Assert.Contains(Runtime, line => line.Trim() == $"{name}:");
        Assert.Contains(Runtime, line => line.Contains($"{name}: ${{{{ secrets.{name} }}}}", StringComparison.Ordinal));
        Assert.Contains(Runtime, line => line.Contains($"--env {name}", StringComparison.Ordinal));
        Assert.Contains(Plugins, line => line.Contains($"{name}: ${{{{ {source} }}}}", StringComparison.Ordinal));
        Assert.Contains($"{name}=%s", Driver, StringComparison.Ordinal);
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

    [Fact]
    public void APrivateNativeFixtureIsCachedAndStrippedWithTheBundlesItPublished()
    {
        Assert.Equal(2, Driver.Split("fixtures=$(private_fixtures) || die").Length - 1);
        Assert.Contains("declare -f private_paths private_fixtures", Driver, StringComparison.Ordinal);
        Assert.Contains("tar --list --file \"$archive\"", Driver, StringComparison.Ordinal);
        Assert.Contains("[[ $path == */native/* && -d $ROOT/$path ]] || continue", Driver, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("scripts/setup-runtime-tests.sh")]
    [InlineData("scripts/runtime-ci.sh")]
    public void OneListOfPackagesServesBothTheToolboxAndTheImage(string script)
    {
        Assert.Contains(Repo.Lines(script), line => line.Contains("runtime-packages.txt"));
    }

    [Fact]
    public void ThePackagesAreInstalledOnAPulledImageToo()
    {
        var steps = string.Join('\n', Job(Runtime, "runtime"))
            .Split("\n      - ", StringSplitOptions.None);

        var prepare = Assert.Single(steps, step => step.Contains("runtime-ci.sh prepare", StringComparison.Ordinal));
        Assert.DoesNotContain(prepare.Split('\n'), line => Names(line, "if"));
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
