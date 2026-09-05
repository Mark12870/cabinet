using Cabinet.Core;

namespace Cabinet.Core.Tests;

public sealed class VirtualDesktopTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("cabinet").FullName;

    private Layout Layout => new(root, "/run/user/1000", Path.Combine(root, "data"));

    public VirtualDesktopTests() => Directory.CreateDirectory(Layout.PrefixPath("gadget"));

    [Fact]
    public void EnablingUsesTheCabinetsFixedDesktopSize()
    {
        var runner = new RecordingRunner();

        new VirtualDesktop(Layout, runner).Set("gadget", null);

        Assert.Equal(
            [
                "--cabinet-join", "reg", "add", @"HKCU\Software\Wine\Explorer\Desktops",
                "/v", "Default", "/d", "1920x1080", "/f",
            ],
            runner.Calls[0].Arguments);
        Assert.Equal(
            [
                "--cabinet-join", "reg", "add", @"HKCU\Software\Wine\Explorer",
                "/v", "Desktop", "/d", "Default", "/f",
            ],
            runner.Calls[1].Arguments);
    }

    [Fact]
    public void AnExistingDesktopWithAnySizeIsEnabled()
    {
        File.WriteAllText(
            Layout.PrefixUserReg("gadget"),
            "[Software\\\\Wine\\\\Explorer]\n\"Desktop\"=\"Default\"\n"
            + "[Software\\\\Wine\\\\Explorer\\\\Desktops]\n\"Default\"=\"1280x720\"\n");

        Assert.True(new VirtualDesktop(Layout, new UnusedRunner()).EnabledIn("gadget"));
    }

    [Fact]
    public void ADesktopWithoutAConfiguredSizeIsOff()
    {
        File.WriteAllText(
            Layout.PrefixUserReg("gadget"),
            "[Software\\\\Wine\\\\Explorer]\n\"Desktop\"=\"Default\"\n");

        Assert.False(new VirtualDesktop(Layout, new UnusedRunner()).EnabledIn("gadget"));
    }

    [Fact]
    public void DisablingRemovesBothDesktopValues()
    {
        var runner = new RecordingRunner();

        new VirtualDesktop(Layout, runner).Unset("gadget", null);

        Assert.Equal(
            [
                "--cabinet-join", "reg", "delete", @"HKCU\Software\Wine\Explorer",
                "/v", "Desktop", "/f",
            ],
            runner.Calls[0].Arguments);
        Assert.Equal(
            [
                "--cabinet-join", "reg", "delete", @"HKCU\Software\Wine\Explorer\Desktops",
                "/v", "Default", "/f",
            ],
            runner.Calls[1].Arguments);
    }

    public void Dispose() => Directory.Delete(root, recursive: true);
}
