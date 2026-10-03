using System.Text;
using Cabinet.Core;

namespace Cabinet.Core.Tests;

public sealed class WindowsInstallerTests : IDisposable
{
    private readonly string root = TestRoot.Create("windows-installer");

    private Layout Layout => new(root, "/run/user/1000", Path.Combine(root, "data"));

    private string Package => Path.Combine(root, "Synth Setup PC.msi");

    private string DriveC => Path.Combine(Layout.PrefixPath("gadget"), "drive_c");

    public WindowsInstallerTests()
    {
        Directory.CreateDirectory(Path.Combine(Layout.PrefixPath("gadget"), "dosdevices"));
        InstallerPackageTests.Synth().Save(Package);
        Payload(Path.Combine("OFFLINE", "AAAA", "Synth.exe"), "synth");
        Payload(Path.Combine("BBBB", "Default.nkm"), "preset");
    }

    [Fact]
    public void ThePackagesFilesLandInThePrefixFromTheSourceItNames()
    {
        new WindowsInstaller(Layout, new RecordingRunner()).Install(
            "gadget", Package, @"ADDLOCAL=ALL SourceDir=C:\payload\data\");

        Assert.Equal("synth", File.ReadAllText(Path.Combine(DriveC, "Program Files", "Vendor", "Synth", "Synth.exe")));
        Assert.Equal(
            "preset",
            File.ReadAllText(Path.Combine(
                DriveC, "Program Files", "Common Files", "Vendor", "Synth", "Presets", "Default.nkm")));
    }

    [Fact]
    public void AFileAlreadyThereIsReplacedWithoutAStagedCopyLeftBeside()
    {
        var installed = Path.Combine(DriveC, "Program Files", "Vendor", "Synth", "Synth.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(installed)!);
        File.WriteAllText(installed, "older");

        new WindowsInstaller(Layout, new RecordingRunner()).Install("gadget", Package, @"SourceDir=C:\payload\data\");

        Assert.Equal("synth", File.ReadAllText(installed));
        Assert.Equal(["Synth.exe"], Directory.GetFiles(Path.GetDirectoryName(installed)!).Select(Path.GetFileName));
    }

    [Fact]
    public void ThePackagesRegistryValuesAreImportedThroughRegedit()
    {
        var recorder = new RecordingRunner();

        new WindowsInstaller(Layout, recorder).Install("gadget", Package, @"SourceDir=C:\payload\data\");

        Assert.Equal(
            "Windows Registry Editor Version 5.00\r\n"
            + "\r\n[HKEY_LOCAL_MACHINE\\SOFTWARE\\Vendor\\Synth]\r\n"
            + "\"InstallDir\"=\"C:\\\\Program Files\\\\Vendor\\\\Synth\\\\\"\r\n"
            + "\"Bits\"=dword:00000040\r\n"
            + "\"Tag\"=\"#1\"\r\n"
            + "\r\n[HKEY_CLASSES_ROOT\\.syn]\r\n"
            + "@=\"Vendor.Synth [1]\"\r\n"
            + "\r\n[HKEY_CURRENT_USER\\Software\\Vendor\\Synth]\r\n",
            File.ReadAllText(Layout.PrefixPackageRegistry("gadget"), Encoding.Unicode));
        Assert.Equal(["regedit", "/S", Layout.PackageRegistry], recorder.LastArguments);
        Assert.Equal(Layout.PrefixPath("gadget"), recorder.Environment["WINEPREFIX"]);
    }

    [Fact]
    public void WithAnEmptySourceDirTheFilesComeFromBesideThePackage()
    {
        Directory.CreateDirectory(Path.Combine(root, "OFFLINE", "AAAA"));
        File.WriteAllText(Path.Combine(root, "OFFLINE", "AAAA", "Synth.exe"), "beside");
        Directory.CreateDirectory(Path.Combine(root, "BBBB"));
        File.WriteAllText(Path.Combine(root, "BBBB", "Default.nkm"), "beside");

        new WindowsInstaller(Layout, new RecordingRunner()).Install("gadget", Package, "SourceDir=");

        Assert.Equal("beside", File.ReadAllText(Path.Combine(DriveC, "Program Files", "Vendor", "Synth", "Synth.exe")));
    }

    [Fact]
    public void APrefixADawIsUsingIsRefusedBeforeAnythingIsPlaced()
    {
        using var plugin = SessionFiles.HeldByAPlugin(SessionFiles.Of(Layout, "gadget").Busy);

        var refused = Assert.Throws<PrefixInUseException>(() => new WindowsInstaller(Layout, new RecordingRunner())
            .Install("gadget", Package, @"SourceDir=C:\payload\data\"));

        Assert.StartsWith("A DAW is using plugins from gadget", refused.Message);
        Assert.False(Directory.Exists(Path.Combine(DriveC, "Program Files", "Vendor")));
    }

    [Fact]
    public void AScriptAlreadyHoldingThePrefixPlacesTheFilesItself()
    {
        using var claim = new Prefixes(Layout, new RecordingRunner()).Claim("gadget", "run a library script");

        new WindowsInstaller(Layout, new RecordingRunner()).Install(
            "gadget", Package, @"SourceDir=C:\payload\data\", claimedBy: Layout.PrefixPath("gadget"));

        Assert.Equal("synth", File.ReadAllText(Path.Combine(DriveC, "Program Files", "Vendor", "Synth", "Synth.exe")));
    }

    [Fact]
    public void AnUnknownPrefixIsRefused() =>
        Assert.Throws<KeyNotFoundException>(
            () => new WindowsInstaller(Layout, new RecordingRunner()).Install("nowhere", Package, ""));

    private void Payload(string relative, string content)
    {
        var path = Path.Combine(DriveC, "payload", "data", relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    public void Dispose() => Directory.Delete(root, recursive: true);
}
