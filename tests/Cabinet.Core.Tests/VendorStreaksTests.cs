using System.Net;
using System.Security.Authentication;
using System.Xml.Linq;
using Cabinet.Ci;

namespace Cabinet.Core.Tests;

public class VendorStreaksTests
{
    private static readonly DateOnly Monday = new(2026, 11, 16);

    [Fact]
    public void OnlyATransientDownloadFailureIsAnOutage()
    {
        Assert.Equal(
            "curl: (22) The requested URL returned error: 503",
            VendorOutage.InInstall("curl: (22) The requested URL returned error: 503\ncabinet: could not download https://example.com/a.zip"));
        Assert.Equal(
            "curl: (28) Operation timed out after 30000 milliseconds",
            VendorOutage.InInstall("curl: (28) Operation timed out after 30000 milliseconds\ncabinet: could not download https://example.com/a.zip"));
        Assert.Null(VendorOutage.InInstall("curl: (22) The requested URL returned error: 404\ncabinet: could not download https://example.com/a.zip"));
        Assert.Null(VendorOutage.InInstall("curl: (22) The requested URL returned error: 403\ncabinet: could not download https://example.com/a.zip"));
        Assert.Null(VendorOutage.InInstall("curl: (28) Operation timed out\nthe installer exited with 1"));
    }

    [Fact]
    public void AnUnansweredOrOverloadedServerIsAnOutageAndARefusalIsNot()
    {
        Assert.NotNull(VendorOutage.Of(new HttpRequestException("Connection refused (example.com:443)")));
        Assert.NotNull(VendorOutage.Of(new HttpRequestException("busy", null, HttpStatusCode.ServiceUnavailable)));
        Assert.NotNull(VendorOutage.Of(new HttpRequestException("slow down", null, HttpStatusCode.TooManyRequests)));
        Assert.NotNull(VendorOutage.Of(new TaskCanceledException("timed out", new TimeoutException())));
        Assert.Null(VendorOutage.Of(new HttpRequestException("gone", null, HttpStatusCode.NotFound)));
        Assert.Null(VendorOutage.Of(new HttpRequestException("blocked", null, HttpStatusCode.Forbidden)));
        Assert.Null(VendorOutage.Of(new TaskCanceledException("cancelled")));
        Assert.Null(VendorOutage.Of(new HttpRequestException("bad certificate", new AuthenticationException())));
    }

    [Fact]
    public void AStreakGrowsWhileTheVendorStaysUnavailableAndEndsWhenItAnswers()
    {
        var previous = new Dictionary<string, Streak>
        {
            ["still-down"] = new(Monday.AddDays(-2), 2, "curl: (28)"),
            ["back-up"] = new(Monday.AddDays(-1), 1, "curl: (7)"),
            ["not-run"] = new(Monday.AddDays(-1), 1, "curl: (6)"),
        };

        var next = VendorStreaks.Next(
            previous,
            new HashSet<string> { "still-down", "back-up", "new-down" },
            new Dictionary<string, string> { ["still-down"] = "curl: (56)", ["new-down"] = "curl: (7)" },
            Monday);

        Assert.Equal(new Streak(Monday.AddDays(-2), 3, "curl: (56)"), next["still-down"]);
        Assert.Equal(new Streak(Monday, 1, "curl: (7)"), next["new-down"]);
        Assert.Equal(previous["not-run"], next["not-run"]);
        Assert.DoesNotContain("back-up", next.Keys);
    }

    [Fact]
    public void AnEntryThatPassedOnAnEarlierAttemptEndsItsStreakAndARemovedEntryIsForgotten()
    {
        var directory = TestRoot.Create(nameof(AnEntryThatPassedOnAnEarlierAttemptEndsItsStreakAndARemovedEntryIsForgotten));
        var results = Path.Combine(directory, "artifact", "TestResults");
        WriteTrx(results, "RetriedScenario");
        WriteTrx(Path.Combine(results, "Retries", "run", "1"), "SurgeXtScenario");
        WriteTrx(Path.Combine(results, "Retries", "run", "2"), "RetriedScenario");
        var previous = Path.Combine(directory, "previous.json");
        File.WriteAllText(previous, VendorStreaks.Write(new Dictionary<string, Streak>
        {
            ["surge-xt"] = new(Monday, 2, "curl: (7)"),
            ["no-such-entry"] = new(Monday, 5, "curl: (7)"),
        }));
        var output = Path.Combine(directory, "streaks.json");

        Assert.Equal(0, VendorStreaks.Update(previous, output, [Path.Combine(directory, "artifact")]));
        Assert.Empty(VendorStreaks.Read(output));
    }

    [Fact]
    public void StreaksSurviveTheTripThroughTheirFile()
    {
        var directory = TestRoot.Create(nameof(StreaksSurviveTheTripThroughTheirFile));
        var path = Path.Combine(directory, "streaks.json");
        var streaks = new Dictionary<string, Streak> { ["vendor-a"] = new(Monday, 2, "curl: (22) error: 502") };

        File.WriteAllText(path, VendorStreaks.Write(streaks));

        Assert.Equal(streaks, VendorStreaks.Read(path));
        Assert.Empty(VendorStreaks.Read(Path.Combine(directory, "missing.json")));
    }

    [Fact]
    public void TheReportNamesEachUnavailableVendorAndTheLimit()
    {
        var report = VendorStreaks.Report(new Dictionary<string, Streak> { ["vendor-a"] = new(Monday, 2, "a | b") });

        Assert.Contains("| `vendor-a` | 2 | 2026-11-16 | a / b |", report, StringComparison.Ordinal);
        Assert.Contains($"{VendorStreaks.Limit} runs in a row", report, StringComparison.Ordinal);
        Assert.Contains("Every vendor answered.", VendorStreaks.Report(new Dictionary<string, Streak>()), StringComparison.Ordinal);
    }

    private static void WriteTrx(string directory, string scenario)
    {
        Directory.CreateDirectory(directory);
        XNamespace ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
        new XDocument(
            new XElement(ns + "TestRun",
                new XElement(ns + "Results",
                    new XElement(ns + "UnitTestResult",
                        new XAttribute("testName", $"Cabinet.Runtime.Tests.Scenarios.{scenario}.OpensItsEditorAndPlaysANote(format: \"VST3\")"),
                        new XAttribute("outcome", "Passed"))))).Save(Path.Combine(directory, "runtime.trx"));
    }
}
