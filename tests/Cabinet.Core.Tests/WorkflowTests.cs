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
        Assert.Contains(merge, line => line.Contains("github.event_name == 'workflow_dispatch'", StringComparison.Ordinal));
        Assert.DoesNotContain(merge, line => line.Contains("gh pr merge", StringComparison.Ordinal));
        Assert.Contains(merge, line => line.Contains("gh api \"repos/$GH_REPO/pulls/$PR\"", StringComparison.Ordinal));
        Assert.Contains(merge, line => line.Contains(".state == \"open\"", StringComparison.Ordinal));
        Assert.Contains(merge, line => line.Contains(".draft == false", StringComparison.Ordinal));
        Assert.Contains(merge, line => line.Contains(".user.login == \"github-actions[bot]\"", StringComparison.Ordinal));
        Assert.Contains(merge, line => line.Contains(".head.sha == $head", StringComparison.Ordinal));
        Assert.Contains(merge, line => line.Contains(".head.repo.full_name == $repo", StringComparison.Ordinal));
        Assert.Contains(merge, line => line.Contains(".head.ref == (\"catalogue-update/\" + $id)", StringComparison.Ordinal));
        Assert.Contains(merge, line => line.Contains(".base.ref == \"main\"", StringComparison.Ordinal));
        Assert.Contains(merge, line => line.Contains(".base.sha == $base", StringComparison.Ordinal));
        Assert.Contains(merge, line => line.Contains(".base.repo.full_name == $repo", StringComparison.Ordinal));
        Assert.Contains(merge, line => line.Contains("repos/$GH_REPO/git/commits/$HEAD", StringComparison.Ordinal));
        Assert.Contains(merge, line => line.Contains(".sha == $head", StringComparison.Ordinal));
        Assert.Contains(merge, line => line.Contains("(.parents | length) == 1", StringComparison.Ordinal));
        Assert.Contains(merge, line => line.Contains(".parents[0].sha == $base", StringComparison.Ordinal));
        Assert.Contains(merge, line => line.Contains("repos/$GH_REPO/git/refs/heads/main", StringComparison.Ordinal));
        Assert.Contains(merge, line => line.Contains("-F force=false", StringComparison.Ordinal));
        Assert.DoesNotContain(merge, line => line.Contains("force=true", StringComparison.Ordinal));
        var pullRequestCheck = merge.ToList().FindIndex(line => line.Contains("pull_request=$(gh api", StringComparison.Ordinal));
        var commitCheck = merge.ToList().FindIndex(line => line.Contains("commit=$(gh api", StringComparison.Ordinal));
        var fastForward = merge.ToList().FindIndex(line => line.Contains("--method PATCH", StringComparison.Ordinal));
        Assert.True(pullRequestCheck >= 0 && pullRequestCheck < commitCheck && commitCheck < fastForward);
        Assert.Contains(merge, line => line.Contains("\"$current\" != \"$BASE\"", StringComparison.Ordinal));
        Assert.Contains(merge, line => line.Contains("GH_TOKEN: ${{ github.token }}", StringComparison.Ordinal));
        Assert.Contains(merge, line => line.Trim() == "actions: write");
        Assert.Contains(merge, line => line.Trim() == "contents: write");
        Assert.Contains(merge, line => line.Trim() == "pull-requests: read");
        var patch = merge.ToList().FindIndex(line => line.Contains("--method PATCH", StringComparison.Ordinal));
        var dispatch = merge.ToList().FindIndex(line => line.Contains("gh workflow run ci.yml --ref main", StringComparison.Ordinal));
        Assert.True(patch >= 0 && dispatch > patch);
        Assert.Contains(runtime, line => line.Contains("entry_id: ${{ needs.candidate.outputs.id }}", StringComparison.Ordinal));
        Assert.Contains(runtime, line => line.Contains("checkout_ref: ${{ needs.candidate.outputs.head }}", StringComparison.Ordinal));
        Assert.Contains(Runtime, line => line.Contains("scenario-approve runtime/TestResults/runtime.trx", StringComparison.Ordinal));
        Assert.Contains(runtime, line => line.Trim() == "packages: read");
    }

    [Fact]
    public void CandidatesAreValidatedUsingTrustedBaseCodeAndOnlyTheEntryAndMetainfo()
    {
        var candidate = Job(Ci, "candidate");

        Assert.DoesNotContain(candidate, line => line.StartsWith("    if:", StringComparison.Ordinal));
        Assert.Contains(candidate, line => line.Contains("ref: ${{ github.sha }}", StringComparison.Ordinal));
        Assert.Contains(candidate, line => line.Contains("persist-credentials: false", StringComparison.Ordinal));
        Assert.Contains(candidate, line => line.Contains("echo \"checkout_sha=$GITHUB_SHA\"", StringComparison.Ordinal));
        Assert.Contains(candidate, line => line.Contains("GH_TOKEN: ${{ github.token }}", StringComparison.Ordinal));
        Assert.Contains(candidate, line => line.Contains("[[ \"$EVENT_NAME\" == workflow_dispatch && \"$REF\" == refs/heads/main", StringComparison.Ordinal));
        Assert.Contains(candidate, line => line.Contains("[[ \"$PR\" =~ ^[1-9][0-9]*$ && \"$HEAD\" =~ ^[0-9a-f]{40}$ ]]", StringComparison.Ordinal));
        Assert.Contains(candidate, line => line.Contains("repos/$REPOSITORY/git/ref/heads/main", StringComparison.Ordinal));
        Assert.Contains(candidate, line => line.Contains(".user.login == \"github-actions[bot]\"", StringComparison.Ordinal));
        Assert.Contains(candidate, line => line.Contains(".head.sha == $head", StringComparison.Ordinal));
        Assert.Contains(candidate, line => line.Contains(".base.sha == $base", StringComparison.Ordinal));
        Assert.Contains(candidate, line => line.Contains("git fetch --no-tags origin \"$HEAD\"", StringComparison.Ordinal));
        Assert.Contains(candidate, line => line.Contains("git rev-list --parents -n1 \"$HEAD\"", StringComparison.Ordinal));
        var ancestry = candidate.ToList().FindIndex(line => line.Contains("git rev-list --parents -n1", StringComparison.Ordinal));
        var diff = candidate.ToList().FindIndex(line => line.Contains("git diff --raw --no-renames", StringComparison.Ordinal));
        Assert.True(ancestry >= 0 && ancestry < diff);
        Assert.Contains(candidate, line => line.Contains("[ \"${#parents[@]}\" -eq 2 ]", StringComparison.Ordinal));
        Assert.Contains(candidate, line => line.Contains("\"${parents[0]}\" = \"$HEAD\"", StringComparison.Ordinal));
        Assert.Contains(candidate, line => line.Contains("\"${parents[1]}\" = \"$BASE\"", StringComparison.Ordinal));
        Assert.Contains(candidate, line => line.Contains("git diff --raw --no-renames", StringComparison.Ordinal));
        Assert.Contains(candidate, line => line.Contains("[ \"${#changed[@]}\" -eq 2 ]", StringComparison.Ordinal));
        Assert.Contains(candidate, line => line.Contains("100644\\ 100644", StringComparison.Ordinal));
        Assert.Contains(candidate, line => line.Contains("flatpak/io.github.mark12870.cabinet.metainfo.xml", StringComparison.Ordinal));
        Assert.Contains(candidate, line => line.Contains("catalogue-updates validate", StringComparison.Ordinal));
        Assert.Contains(candidate, line => line.Contains("validate \"$baseline\" \"$id\"", StringComparison.Ordinal));
        Assert.Contains(candidate, line => line.Contains("echo \"checkout_sha=$HEAD\"", StringComparison.Ordinal));
    }

    [Fact]
    public void CatalogueUpdateProposalsGenerateAndStageTheirNextReleaseEntry()
    {
        var workflow = Repo.Lines(".github/workflows/catalogue-updates.yml");
        var propose = Job(workflow, "propose");

        Assert.Contains(propose, line => line.Contains("dotnet build tools/Cabinet.Ci", StringComparison.Ordinal));
        Assert.Contains(propose, line => line.Contains("catalogue-updates release", StringComparison.Ordinal));
        Assert.Contains(propose, line => line.Contains("date -u +%F", StringComparison.Ordinal));
        Assert.Contains(propose, line => line.Contains("id: pr", StringComparison.Ordinal));
        Assert.Contains(propose, line => line.Contains("token: ${{ github.token }}", StringComparison.Ordinal));
        Assert.Contains(propose, line => line.Contains("sign-commits: true", StringComparison.Ordinal));
        Assert.Contains(propose, line => line.Trim() == "add-paths: |");
        Assert.Contains(propose, line => line.Trim() == "flatpak/io.github.mark12870.cabinet.metainfo.xml");
        Assert.Contains(propose, line => line.Contains("Cabinet patch", StringComparison.Ordinal));
        Assert.Contains(propose, line => line.Contains("existing release gate", StringComparison.Ordinal));
        Assert.Contains(propose, line => line.Contains("normal checks, build and runtime suite", StringComparison.Ordinal));
        Assert.Contains(propose, line => line.Contains("steps.pr.outputs.pull-request-head-sha", StringComparison.Ordinal));
        Assert.Contains(propose, line => line.Contains("gh workflow run ci.yml --ref main -f candidate_pr=\"$PR\" -f candidate_head=\"$HEAD\"", StringComparison.Ordinal));
        Assert.Contains(propose, line => line.Contains(".state == \"open\" and .head.sha == $head", StringComparison.Ordinal));
        Assert.Contains(propose, line => line.Trim() == "actions: write");
        Assert.Contains(propose, line => line.Trim() == "contents: write");
        Assert.Contains(propose, line => line.Trim() == "pull-requests: write");
    }

    [Fact]
    public void DiscoveryReportsRunWithoutWriterCredentialsAndSurviveSourceFailures()
    {
        var workflow = Repo.Lines(".github/workflows/catalogue-updates.yml");
        var discover = Job(workflow, "discover");
        var propose = Job(workflow, "propose");

        Assert.Contains(discover, line => line.Trim() == "if: always()");
        Assert.Contains(discover, line => line.Contains("exit \"$status\"", StringComparison.Ordinal));
        Assert.Contains(propose, line => line.Trim() == "!cancelled()");
    }

    [Fact]
    public void CandidateRunsCheckoutTheirValidatedHeadAndCannotPublish()
    {
        var checks = Job(Ci, "checks");
        var build = Job(Ci, "build");
        var runtime = Job(Ci, "candidate-runtime");
        var normalRuntime = Job(Ci, "runtime");
        var publish = Job(Ci, "publish");

        Assert.Contains(checks, line => line.Contains("ref: ${{ needs.candidate.outputs.checkout_sha }}", StringComparison.Ordinal));
        Assert.Contains(build, line => line.Contains("checkout_ref: ${{ needs.candidate.outputs.checkout_sha }}", StringComparison.Ordinal));
        Assert.Contains(runtime, line => line.Contains("checkout_ref: ${{ needs.candidate.outputs.head }}", StringComparison.Ordinal));
        Assert.Contains(normalRuntime, line => line.Contains("!inputs.candidate_pr && !inputs.candidate_head", StringComparison.Ordinal));
        Assert.Contains(publish, line => line.Contains("needs.checks.result == 'success'", StringComparison.Ordinal));
        Assert.Contains(publish, line => line.Contains("!inputs.candidate_pr && !inputs.candidate_head", StringComparison.Ordinal));
        Assert.Contains(publish, line => line.Contains("needs.candidate.outputs.id == ''", StringComparison.Ordinal));
        Assert.Contains(Ci, line => line.Contains("inputs.candidate_pr && format('candidate-{0}', inputs.candidate_pr)", StringComparison.Ordinal));
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
