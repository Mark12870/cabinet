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

        Assert.Null(watch.Changed(Set("LABS.vst3")));
        Assert.Null(watch.Changed(Set("LABS.vst3")));
    }

    [Fact]
    public void ABundleIsReportedOnlyOnceItHasStoppedChanging()
    {
        var watch = new PluginWatch(Set());

        Assert.Null(watch.Changed(Set("BBCSO.vst3")));
        var change = watch.Changed(Set("BBCSO.vst3"));
        watch.Accept();

        Assert.Equal(["BBCSO.vst3"], change?.Appeared);
        Assert.Null(watch.Changed(Set("BBCSO.vst3")));
    }

    [Fact]
    public void ASecondInstallIsReportedWithoutTheFirst()
    {
        var watch = new PluginWatch(Set());

        watch.Changed(Set("LABS.vst3"));
        var change = watch.Changed(Set("LABS.vst3"));
        watch.Accept();

        Assert.Equal(["LABS.vst3"], change?.Appeared);

        watch.Changed(Set("LABS.vst3", "BBCSO.vst3"));
        change = watch.Changed(Set("LABS.vst3", "BBCSO.vst3"));
        watch.Accept();

        Assert.Equal(["BBCSO.vst3"], change?.Appeared);
    }

    [Fact]
    public void ABundleThatWentAwayIsReportedSoItsBridgeIsPruned()
    {
        var watch = new PluginWatch(Set("LABS.vst3"));

        watch.Changed(Set());

        var change = watch.Changed(Set());

        Assert.NotNull(change);
        Assert.Equal(["LABS.vst3"], change.Gone);
        Assert.Empty(change.Appeared);
        watch.Accept();
        Assert.Null(watch.Changed(Set()));
    }

    [Fact]
    public void AnInstallAndAnUninstallThatSettleTogetherAreBothReported()
    {
        var watch = new PluginWatch(Set("LABS.vst3"));

        watch.Changed(Set("BBCSO.vst3"));

        var change = watch.Changed(Set("BBCSO.vst3"));

        Assert.NotNull(change);
        Assert.Equal(["BBCSO.vst3"], change.Appeared);
        Assert.Equal(["LABS.vst3"], change.Gone);
        watch.Accept();
    }

    [Fact]
    public void ClosingReportsWhatLandedTooLateToSettle()
    {
        var watch = new PluginWatch(Set());

        Assert.Null(watch.Changed(Set("BBCSO.vst3")));
        var change = watch.Closed(Set("BBCSO.vst3"));
        Assert.Equal(["BBCSO.vst3"], change?.Appeared);
    }

    [Fact]
    public void ClosingReportsWhatWentAwayTooLateToSettle()
    {
        var watch = new PluginWatch(Set("LABS.vst3"));

        Assert.Null(watch.Changed(Set()));
        var change = watch.Closed(Set());
        Assert.Equal(["LABS.vst3"], change?.Gone);
    }

    [Fact]
    public void ClosingOnAPrefixNothingReachedReportsNothing()
    {
        var watch = new PluginWatch(Set("LABS.vst3"));

        Assert.Null(watch.Closed(Set("LABS.vst3")));
    }
}
