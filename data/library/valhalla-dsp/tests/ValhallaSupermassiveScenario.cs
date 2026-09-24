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
            "https://valhallaproduction.s3.us-west-2.amazonaws.com/supermassive/ValhallaSupermassiveWin_V5_0_0.zip",
            display);

        var vst2 = scenario.WindowsPlugin(".vst", "ValhallaSupermassive_x64.so");
        scenario.VerifyEditor(vst2, "vst2");
        Reverberates(await scenario.Render(vst2, "vst2", display));

        var vst3 = scenario.WindowsPlugin(".vst3", "ValhallaSupermassive.vst3");
        scenario.VerifyEditor(vst3, "vst3");
        Reverberates(await scenario.Render(vst3, "vst3", display));
    }

    private static void Reverberates(AudioMeasurement audio)
    {
        Assert.True(audio.Parameters > 0, "Valhalla Supermassive exposed no parameters");
        Assert.True(audio.MixChanged, "Valhalla Supermassive's Mix did not hold fully wet");
        Assert.InRange(audio.Before, 0, 0.00001);
        Assert.InRange(audio.Tail, 0.0025, 1);
        Assert.InRange(audio.Peak, 0.014, 1);
    }

    public void Dispose()
    {
        scenario.Dispose();
        runtimeLock.Dispose();
    }
}
