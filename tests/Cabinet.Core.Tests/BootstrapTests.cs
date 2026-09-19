using Cabinet.Core;

namespace Cabinet.Core.Tests;

public sealed class BootstrapTests : IDisposable
{
    private readonly string root = TestRoot.Create("bootstrap");

    [Fact]
    public void ThePrivateYabridgeLinkIsCreatedWhenTheLegacyLinkAlreadyExists()
    {
        var data = Path.Combine(root, "data");
        var hostFiles = Path.Combine(root, "flatpak", "files");
        var bundled = Path.Combine(root, "bundled");
        var hostYabridge = Path.Combine(hostFiles, "lib", "yabridge");
        Directory.CreateDirectory(hostYabridge);
        Directory.CreateDirectory(bundled);
        File.WriteAllText(Path.Combine(bundled, "libyabridge-chainloader-vst3.so"), "bridge");
        var layout = new Layout(
            root, Path.Combine(root, "runtime"), data, hostFiles, yabridgeDir: bundled);
        Directory.CreateDirectory(Path.GetDirectoryName(layout.SandboxYabridgeLink)!);
        File.CreateSymbolicLink(layout.SandboxYabridgeLink, layout.HostYabridgeDir);

        Bootstrap.Ensure(layout);

        Assert.Equal(layout.HostYabridgeDir, new DirectoryInfo(layout.SandboxYabridgeLink).LinkTarget);
        Assert.Equal(bundled, new DirectoryInfo(layout.BridgeYabridgeLink).LinkTarget);
        Assert.False(Path.Exists(layout.CabinetScanDir(".vst3")));
    }

    [Fact]
    public void BootstrapMovesTheLegacyScanLinks()
    {
        var layout = new Layout(root, Path.Combine(root, "runtime"), Path.Combine(root, "data"));
        var bundle = Path.Combine(layout.NativePath("thing"), "Thing.vst3");
        Directory.CreateDirectory(bundle);
        Directory.CreateDirectory(layout.BridgeOutputDir(".vst3"));
        Directory.CreateDirectory(layout.ScanDir(".vst3"));
        File.CreateSymbolicLink(layout.CabinetScanDir(".vst3"), layout.BridgeOutputDir(".vst3"));
        File.CreateSymbolicLink(Path.Combine(layout.ScanDir(".vst3"), "Thing.vst3"), bundle);

        Bootstrap.Ensure(layout);

        Assert.Equal([layout.CabinetScanDir(".vst3")], Directory.EnumerateFileSystemEntries(layout.ScanDir(".vst3")));
        Assert.Equal(
            layout.BridgeOutputDir(".vst3"),
            new DirectoryInfo(layout.WindowsScanDir(".vst3")).LinkTarget);
        Assert.Equal(bundle, new DirectoryInfo(Path.Combine(layout.NativeScanDir(".vst3"), "Thing.vst3")).LinkTarget);
    }

    public void Dispose() => Directory.Delete(root, recursive: true);
}
