using Cabinet.Core;

namespace Cabinet.Core.Tests;

public partial class LibraryTests
{
    [Fact]
    public void TheManagersPluginsAreListedByNameWithTheirFormatsAndNotTheirLibraries()
    {
        var layout = Layout();
        var entry = Manager();
        Recorded(layout, entry);
        var vst2 = PluginDir(layout, entry, "VST2");
        File.WriteAllText(Path.Combine(PluginDir(layout, entry, "VST3"), "Thing.vst3"), "");
        File.WriteAllText(Path.Combine(vst2, "Thing.dll"), "VSTPluginMain");
        File.WriteAllText(Path.Combine(vst2, "helper.dll"), "library");

        var plugins = new Library(layout, new RecordingRunner()).PluginsOf(entry);

        var plugin = Assert.Single(plugins);
        Assert.Equal("Thing", plugin.Name);
        Assert.Equal(["VST2", "VST3"], plugin.Formats);
    }

    [Fact]
    public void ARemovedPluginTakesItsContentAndLeavesTheOthers()
    {
        var layout = Layout();
        var entry = Manager();
        Recorded(layout, entry);
        var vst2 = PluginDir(layout, entry, "VST2");
        File.WriteAllText(Path.Combine(vst2, "Thing.dll"), "VSTPluginMain");
        File.WriteAllText(Path.Combine(vst2, "Thing.instruments.zip"), "content");
        File.WriteAllText(Path.Combine(vst2, "Other.dll"), "VSTPluginMain");
        var recorder = new RecordingRunner();

        new Library(layout, recorder).RemovePlugin(entry, "Thing", _ => { });

        Assert.Equal(["Other.dll"], Directory.EnumerateFiles(vst2).Select(Path.GetFileName));
        Assert.Contains(recorder.Calls, Synced);
    }

    [Fact]
    public void ARemovedPluginsOwnUninstallerRunsWithoutAsking()
    {
        var layout = Layout();
        var entry = Manager();
        Recorded(layout, entry);
        File.WriteAllText(Path.Combine(PluginDir(layout, entry, "VST3"), "Thing.vst3"), "");
        File.WriteAllText(layout.PrefixSystemReg(entry.Prefix), """
            WINE REGISTRY Version 2

            [Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\Thing_is1] 1787344290
            "DisplayName"="Thing version 1.2"
            "UninstallString"="C:\\Thing\\unins000.exe"
            """);
        var recorder = new RecordingRunner();

        new Library(layout, recorder).RemovePlugin(entry, "Thing", _ => { });

        Assert.Contains(
            recorder.Calls,
            call => call.Arguments.SequenceEqual(
                [Prefixes.JoinMode, "cmd", "/c", @"C:\Thing\unins000.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART"]));
    }

    private static string PluginDir(Layout layout, LibraryEntry entry, string format) =>
        Directory.CreateDirectory(
            layout.PrefixPluginDirs(entry.Prefix).First(directory => Path.GetFileName(directory) == format)).FullName;
}
