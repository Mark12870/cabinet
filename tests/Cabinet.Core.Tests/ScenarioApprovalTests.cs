using System.Xml.Linq;
using Cabinet.Ci;

namespace Cabinet.Core.Tests;

public class ScenarioApprovalTests
{
    private const string EntryId = "surge-xt";
    private const string Scenario = "SurgeXtScenario";
    private const string Test = "OpensItsEditorAndPlaysANote";
    private static readonly string[] Formats = ["VST3", "CLAP", "LV2"];

    [Fact]
    public void CompleteRequiresEveryDeclaredFormatToPassWithoutAnUnexpectedFormat()
    {
        Assert.True(ScenarioReport.Complete(Formats, Outcomes(("VST3", "Passed"), ("CLAP", "Passed"), ("LV2", "Passed"))));
        Assert.False(ScenarioReport.Complete(Formats, Outcomes(("VST3", "Passed"), ("CLAP", "Passed"))));
        Assert.False(ScenarioReport.Complete(Formats, Outcomes(("VST3", "Passed"), ("CLAP", "Skipped"), ("LV2", "Passed"))));
        Assert.False(ScenarioReport.Complete(Formats, Outcomes(("VST3", "Passed"), ("CLAP", "Failed"), ("LV2", "Passed"))));
        Assert.False(ScenarioReport.Complete(Formats, Outcomes(("VST3", "Passed"), ("CLAP", "Passed on rerun"), ("LV2", "Passed"))));
        Assert.False(ScenarioReport.Complete(
            Formats,
            Outcomes(("VST3", "Passed"), ("CLAP", "Passed"), ("LV2", "Passed"), ("Unexpected", "Passed"))));
        Assert.False(ScenarioReport.Complete([], new Dictionary<string, string>()));
        Assert.False(ScenarioReport.Complete([""], Outcomes(("", "Passed"))));
        Assert.False(ScenarioReport.Complete(["VST3", "VST3"], Outcomes(("VST3", "Passed"), ("LV2", "Passed"))));
    }

    [Fact]
    public void ApprovalRejectsMissingFormats()
    {
        var directory = TestRoot.Create(nameof(ApprovalRejectsMissingFormats));
        var trx = WriteTrx(directory, [("VST3", "Passed"), ("CLAP", "Passed")]);

        Assert.Equal(1, ScenarioReport.Approve(trx, EntryId));
    }

    [Fact]
    public void ApprovalRejectsSkippedFormats()
    {
        var directory = TestRoot.Create(nameof(ApprovalRejectsSkippedFormats));
        var trx = WriteTrx(directory, [("VST3", "Passed"), ("CLAP", "Skipped"), ("LV2", "Passed")]);

        Assert.Equal(1, ScenarioReport.Approve(trx, EntryId));
    }

    [Fact]
    public void ApprovalRejectsFailedFormats()
    {
        var directory = TestRoot.Create(nameof(ApprovalRejectsFailedFormats));
        var trx = WriteTrx(directory, [("VST3", "Passed"), ("CLAP", "Passed"), ("LV2", "Failed")]);

        Assert.Equal(1, ScenarioReport.Approve(trx, EntryId));
    }

    [Fact]
    public void APassingCaseCannotHideASkippedCaseOfTheSameFormat()
    {
        var directory = TestRoot.Create(nameof(APassingCaseCannotHideASkippedCaseOfTheSameFormat));
        var trx = WriteTrx(directory, [("VST3", "Skipped"), ("VST3", "Passed"), ("CLAP", "Passed"), ("LV2", "Passed")]);

        Assert.Equal(1, ScenarioReport.Approve(trx, EntryId));
    }

    [Fact]
    public void ApprovalRejectsAFormatThatPassesOnlyOnRetry()
    {
        var directory = TestRoot.Create(nameof(ApprovalRejectsAFormatThatPassesOnlyOnRetry));
        var trx = WriteTrx(directory, [("VST3", "Passed"), ("CLAP", "Passed"), ("LV2", "Passed")]);
        var firstAttempt = WriteTrx(
            Path.Combine(Path.GetDirectoryName(trx)!, "Retries", "Scenario", "0"),
            [("VST3", "Passed"), ("CLAP", "Passed"), ("LV2", "Failed")]);
        var retryAttempt = WriteTrx(
            Path.Combine(Path.GetDirectoryName(trx)!, "Retries", "Scenario", "1"),
            [("LV2", "Passed")]);

        Assert.NotEqual(trx, firstAttempt);
        Assert.NotEqual(trx, retryAttempt);
        Assert.Equal(1, ScenarioReport.Approve(trx, EntryId));
    }

    [Fact]
    public void ApprovalAcceptsAllFormatsPassingOnTheirFirstAttempt()
    {
        var directory = TestRoot.Create(nameof(ApprovalAcceptsAllFormatsPassingOnTheirFirstAttempt));
        var trx = WriteTrx(directory, [("VST3", "Passed"), ("CLAP", "Passed"), ("LV2", "Passed")]);

        Assert.Equal(0, ScenarioReport.Approve(trx, EntryId));
    }

    [Theory]
    [InlineData("Skipped", 1)]
    [InlineData("Failed", 1)]
    [InlineData("Passed", 0)]
    public void ApprovalAlsoRequiresNonFormatCasesToPass(string outcome, int expected)
    {
        var directory = TestRoot.Create(nameof(ApprovalAlsoRequiresNonFormatCasesToPass) + outcome);
        var trx = WriteTrx(directory, [("VST3", "Passed"), ("CLAP", "Passed"), ("LV2", "Passed")]);
        var document = XDocument.Load(trx);
        XNamespace ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
        document.Root!.Element(ns + "Results")!.Add(new XElement(ns + "UnitTestResult",
            new XAttribute("testName", $"Cabinet.Runtime.Tests.Scenarios.{Scenario}.OpensAnotherEditor"),
            new XAttribute("outcome", outcome)));
        document.Save(trx);

        Assert.Equal(expected, ScenarioReport.Approve(trx, EntryId));
    }

    private static Dictionary<string, string> Outcomes(params (string Format, string Outcome)[] results) =>
        results.ToDictionary(result => result.Format, result => result.Outcome, StringComparer.Ordinal);

    private static string WriteTrx(string directory, IReadOnlyList<(string Format, string Outcome)> results)
    {
        Directory.CreateDirectory(directory);
        XNamespace ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
        var path = Path.Combine(directory, "runtime.trx");
        new XDocument(
            new XElement(ns + "TestRun",
                new XElement(ns + "Results",
                    results.Select(result => new XElement(ns + "UnitTestResult",
                        new XAttribute("testName", $"Cabinet.Runtime.Tests.Scenarios.{Scenario}.{Test}(format: \"{result.Format}\")"),
                        new XAttribute("outcome", result.Outcome)))))).Save(path);
        return path;
    }
}
