using Cabinet.Core;

namespace Cabinet.Core.Tests;

public sealed class DownloadsTests : IDisposable
{
    private readonly string root = TestRoot.Create("downloads");

    public void Dispose() => Directory.Delete(root, recursive: true);

    [Fact]
    public void AFileIsKeptOnlyOnceItHasStoppedChanging()
    {
        var source = Directory.CreateDirectory(Path.Combine(root, "downloads")).FullName;
        var file = Path.Combine(source, "product.zip");
        var recorder = new RecordingRunner();
        var downloads = new Downloads(source, Path.Combine(root, "kept"), recorder);
        File.WriteAllText(file, "half");

        var first = downloads.Hold(_ => { });
        File.AppendAllText(file, " and the rest");
        var growing = downloads.Hold(_ => { });
        var steady = downloads.Hold(_ => { });

        Assert.Equal([false, false, true], [first, growing, steady]);
        Assert.False(downloads.Arriving);
    }

    [Fact]
    public void AStalledFileNotYetGivenItsNameIsStillArriving()
    {
        var source = Directory.CreateDirectory(Path.Combine(root, "downloads")).FullName;
        File.WriteAllText(Path.Combine(source, "1795600000123"), "half");
        var downloads = new Downloads(source, Path.Combine(root, "kept"), new RecordingRunner());

        var first = downloads.Hold(_ => { });
        var stalled = downloads.Hold(_ => { });

        Assert.Equal([false, false], [first, stalled]);
        Assert.True(downloads.Arriving);
    }

    [Fact]
    public void AnUnnamedFileThatStoppedGrowingLongAgoIsNotArriving()
    {
        var source = Directory.CreateDirectory(Path.Combine(root, "downloads")).FullName;
        File.WriteAllText(Path.Combine(source, "1795600000123"), "half");
        File.SetLastWriteTimeUtc(Path.Combine(source, "1795600000123"), DateTime.UtcNow.AddHours(-1));
        var downloads = new Downloads(source, Path.Combine(root, "kept"), new RecordingRunner());

        Assert.False(downloads.Arriving);
    }

    [Fact]
    public void AnAppThatFinishesAfterClosingHasEveryFileKeptAtOnce()
    {
        var source = Directory.CreateDirectory(Path.Combine(root, "downloads")).FullName;
        File.WriteAllText(Path.Combine(source, "1795600000123"), "half");
        var downloads = new Downloads(source, Path.Combine(root, "kept"), new RecordingRunner(), atOnce: true);

        Assert.True(downloads.Hold(_ => { }));
    }

    [Fact]
    public void AnUnnamedFileIsKeptOnceTheAppHasClosed()
    {
        var source = Directory.CreateDirectory(Path.Combine(root, "downloads")).FullName;
        File.WriteAllText(Path.Combine(source, "1795600000123"), "half");
        var downloads = new Downloads(source, Path.Combine(root, "kept"), new RecordingRunner());

        Assert.True(downloads.Hold(_ => { }, settled: true));
    }

    [Fact]
    public void WhatIsStillArrivingIsCountedInBytes()
    {
        var source = Directory.CreateDirectory(Path.Combine(root, "downloads")).FullName;
        File.WriteAllText(Path.Combine(source, "6975"), "twelve bytes");
        var downloads = new Downloads(source, Path.Combine(root, "kept"), new RecordingRunner());

        Assert.True(downloads.Arriving);
        Assert.Equal(12, downloads.Bytes);
    }

    [Theory]
    [InlineData("Thing.vst3", "Thing (VST3)")]
    [InlineData("Thing.clap", "Thing (CLAP)")]
    [InlineData("Thing.dll", "Thing (VST2)")]
    public void APluginIsNamedWithItsFormat(string bundle, string ready) =>
        Assert.Equal(ready, Downloads.Ready(Path.Combine(root, bundle)));
}
