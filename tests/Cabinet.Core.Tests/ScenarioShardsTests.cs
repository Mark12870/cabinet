using Cabinet.Ci;

namespace Cabinet.Core.Tests;

public class ScenarioShardsTests
{
    private static readonly Dictionary<string, double> Spent = new()
    {
        ["Slowest"] = 700,
        ["Slow"] = 400,
        ["Middle"] = 300,
        ["Quick"] = 30,
        ["Quicker"] = 20,
        ["Quickest"] = 10,
    };

    [Fact]
    public void SummarisesEachShardsExpectedMinutesAndWhatStartsIt()
    {
        var summary = ScenarioShards.Summary(ScenarioShards.Deal(Spent, [.. Spent.Keys], 3), timed: true);

        Assert.Contains("at most 12 min of scenario time per shard, run four at once", summary, StringComparison.Ordinal);
        Assert.Contains("| 0 | 12 min | 1 | `Slowest` |", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void SaysTheScenariosWereDealtByNameWhenNoRunLeftTimings()
    {
        var summary = ScenarioShards.Summary(ScenarioShards.Deal(new Dictionary<string, double>(), ["A", "B"], 2), timed: false);

        Assert.Contains("no earlier run left timings, so the scenarios were dealt by name", summary, StringComparison.Ordinal);
        Assert.DoesNotContain("0 min", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void DealsTheLongestScenariosFirstToTheLightestShard()
    {
        var shards = ScenarioShards.Deal(Spent, [.. Spent.Keys], 3);

        Assert.Equal(["Slowest"], shards[0].Classes);
        Assert.Equal(["Slow"], shards[1].Classes);
        Assert.Equal(["Middle", "Quick", "Quicker", "Quickest", "ImapOtpTests"], shards[2].Classes);
    }

    [Fact]
    public void StartsAScenarioWithoutTimingsAsIfItWereTheSlowest()
    {
        var shards = ScenarioShards.Deal(Spent, ["New", .. Spent.Keys], 2);

        Assert.Equal(["New", "Slow"], shards[0].Classes);
        Assert.Equal(["Slowest", "Middle", "Quick", "Quicker", "Quickest", "ImapOtpTests"], shards[1].Classes);
    }

    [Fact]
    public void DealsByNameWhenNoRunHasTimings()
    {
        var shards = ScenarioShards.Deal(new Dictionary<string, double>(), ["C", "A", "B", "D"], 2);

        Assert.Equal(["A", "C", "ImapOtpTests"], shards[0].Classes);
        Assert.Equal(["B", "D"], shards[1].Classes);
    }
}
