namespace Cabinet.Core.Tests;

public class DoctorTests
{
    private static IniFile Info(string app, string runtime) =>
        IniFile.Parse(["[Instance]", $"app-extensions={app}", $"runtime-extensions={runtime}"]);

    [Fact]
    public void MountedCompatAndMatchingGraphicsPass()
    {
        var checks = Doctor.ThirtyTwoBit(Info(
            "org.freedesktop.Platform.Compat.i386=aa;org.freedesktop.Platform.GL32.default=bb",
            "org.freedesktop.Platform.GL.default=cc;org.freedesktop.Platform.GL.default=dd"));

        Assert.All(checks, check => Assert.Equal(Status.Ok, check.Status));
    }

    [Fact]
    public void MissingCompatFailsAndPointsAtAnUpdate()
    {
        var compat = Doctor.ThirtyTwoBit(Info(
            "org.winehq.Wine.gecko=aa",
            "org.freedesktop.Platform.GL.default=cc")).First();

        Assert.Equal(Status.Fail, compat.Status);
        Assert.Contains("flatpak update io.github.mark12870.cabinet", compat.Detail);
    }

    [Fact]
    public void NoAppExtensionsAtAllStillFails()
    {
        var info = IniFile.Parse(
            ["[Instance]", "runtime-extensions=org.freedesktop.Platform.GL.default=cc"]);

        var checks = Doctor.ThirtyTwoBit(info).ToList();

        Assert.Equal([Status.Fail, Status.Warn], checks.Select(check => check.Status));
    }

    [Fact]
    public void GraphicsWithoutTheirThirtyTwoBitHalfNameItOnce()
    {
        var graphics = Doctor.ThirtyTwoBit(Info(
            "org.freedesktop.Platform.Compat.i386=aa",
            "org.freedesktop.Platform.GL.nvidia-580-82-09=cc;org.freedesktop.Platform.GL.nvidia-580-82-09=dd"))
            .Last();

        Assert.Equal(Status.Warn, graphics.Status);
        Assert.Equal(
            "32-bit plugins cannot draw their editors — run "
            + "`flatpak install flathub org.freedesktop.Platform.GL32.nvidia-580-82-09`",
            graphics.Detail);
    }

    [Fact]
    public void OutsideAFlatpakThereIsNothingToCheck()
    {
        Assert.Empty(Doctor.ThirtyTwoBit(IniFile.Empty));
    }
}
