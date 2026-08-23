using Cabinet.Core;

namespace Cabinet.Core.Tests;

public class PluginWatchTests
{
    private static IReadOnlySet<string> Set(params string[] bundles) =>
        bundles.ToHashSet(StringComparer.Ordinal);

    [Fact]
    public void APrefixThatNeverChangesNeverAsksToBeBridged()
    {
        var watch = new PluginWatch(Set("LABS.vst3"));

        Assert.Null(watch.Appeared(Set("LABS.vst3")));
        Assert.Null(watch.Appeared(Set("LABS.vst3")));
    }

    [Fact]
    public void ABundleIsReportedOnlyOnceItHasStoppedChanging()
    {
        var watch = new PluginWatch(Set());

        Assert.Null(watch.Appeared(Set("BBCSO.vst3")));
        Assert.Equal(["BBCSO.vst3"], watch.Appeared(Set("BBCSO.vst3")));
        Assert.Null(watch.Appeared(Set("BBCSO.vst3")));
    }

    [Fact]
    public void ASecondInstallIsReportedWithoutTheFirst()
    {
        var watch = new PluginWatch(Set());

        watch.Appeared(Set("LABS.vst3"));
        Assert.Equal(["LABS.vst3"], watch.Appeared(Set("LABS.vst3")));

        watch.Appeared(Set("LABS.vst3", "BBCSO.vst3"));
        Assert.Equal(["BBCSO.vst3"], watch.Appeared(Set("LABS.vst3", "BBCSO.vst3")));
    }

    [Fact]
    public void ABundleThatWentAwayIsNotReportedAsHavingArrived()
    {
        var watch = new PluginWatch(Set("LABS.vst3"));

        watch.Appeared(Set());
        Assert.Null(watch.Appeared(Set()));
    }

    [Fact]
    public void ClosingReportsWhatLandedTooLateToSettle()
    {
        var watch = new PluginWatch(Set());

        Assert.Null(watch.Appeared(Set("BBCSO.vst3")));
        Assert.Equal(["BBCSO.vst3"], watch.Closed(Set("BBCSO.vst3")));
    }

    [Fact]
    public void ClosingOnAPrefixNothingReachedReportsNothing()
    {
        var watch = new PluginWatch(Set("LABS.vst3"));

        Assert.Null(watch.Closed(Set("LABS.vst3")));
    }
}
