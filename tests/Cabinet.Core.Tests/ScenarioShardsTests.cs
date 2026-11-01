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
    public void DealsTheLongestScenariosFirstToTheLightestShard()
    {
        var shards = ScenarioShards.Deal(Spent, [.. Spent.Keys], 3);

        Assert.Equal(["Slowest"], shards[0]);
        Assert.Equal(["Slow"], shards[1]);
        Assert.Equal(["Middle", "Quick", "Quicker", "Quickest", "ImapOtpTests"], shards[2]);
    }

    [Fact]
    public void StartsAScenarioWithoutTimingsAsIfItWereTheSlowest()
    {
        var shards = ScenarioShards.Deal(Spent, ["New", .. Spent.Keys], 2);

        Assert.Equal(["New", "Slow"], shards[0]);
        Assert.Equal(["Slowest", "Middle", "Quick", "Quicker", "Quickest", "ImapOtpTests"], shards[1]);
    }

    [Fact]
    public void DealsByNameWhenNoRunHasTimings()
    {
        var shards = ScenarioShards.Deal(new Dictionary<string, double>(), ["C", "A", "B", "D"], 2);

        Assert.Equal(["A", "C", "ImapOtpTests"], shards[0]);
        Assert.Equal(["B", "D"], shards[1]);
    }
}
