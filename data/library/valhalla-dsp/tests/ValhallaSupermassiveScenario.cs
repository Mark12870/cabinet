namespace Cabinet.Runtime.Tests;

public sealed class ValhallaSupermassiveScenario : IDisposable
{
    private readonly RuntimeTestLock runtimeLock = RuntimeTestLock.Acquire();
    private readonly PluginScenario scenario = new("valhalla-supermassive");

    [Fact]
    public async Task DownloadsInstallsOpensAndProcessesAudio()
    {
        scenario.Prepare();
        using var display = Display.Start();
        await scenario.Install(
            "valhalla-supermassive",
            "https://valhallaproduction.s3.us-west-2.amazonaws.com/supermassive/ValhallaSupermassiveWin_V5_0_0.zip",
            display);

        var plugin = scenario.WindowsPlugin(".vst", "ValhallaSupermassive_x64.so");
        scenario.VerifyEditor("scenario-valhalla-supermassive", plugin, "vst2");

        var audio = await scenario.Render(plugin, "vst2", display);
        Assert.True(audio.MixChanged, "Valhalla Supermassive exposed no writable Mix parameter");
        Assert.True(audio.Parameters > 0, "Valhalla Supermassive exposed no parameters");
        Assert.Equal(244800, audio.Frames);
        Assert.InRange(audio.Before, 0, 0.00001);
        Assert.InRange(audio.Signal, 0, 2);
        Assert.InRange(audio.Tail, 0.00001, 2);
        Assert.InRange(audio.Peak, 0.00001, 4);
    }

    public void Dispose()
    {
        scenario.Dispose();
        runtimeLock.Dispose();
    }
}
