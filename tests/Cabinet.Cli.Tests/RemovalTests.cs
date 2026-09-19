using Cabinet.Core;

namespace Cabinet.Cli.Tests;

public sealed class RemovalTests : IDisposable
{
    private const string Plugin = "Name: Thing\nKind: windows\nSource: byo\n";
    private const string Manager = Plugin + @"Launch: C:\Program Files\Thing\Thing.exe" + "\n";

    private readonly Cli cli = new();

    public void Dispose() => cli.Dispose();

    [Fact]
    public void AManagerAsksOnlyWhetherToDeleteItsPrefix()
    {
        var where = Installed(Manager, "thing");

        var outcome = cli.Answer("n\n", "library", "remove", "thing");

        Assert.Equal(3, outcome.Exit);
        Assert.Equal(
            $"Thing's own uninstaller leaves everything it downloaded in prefix '{where}', "
            + "so it is the prefix or nothing.\n"
            + $"Delete '{where}' and every library Thing put in it? [y/N] Left alone.\n",
            outcome.Error);
        Assert.Empty(outcome.Out);
        Assert.True(Directory.Exists(cli.Layout.PrefixPath(where)));
    }

    [Fact]
    public void AManagerNamesThePluginsThatGoWithItsPrefix()
    {
        var where = Installed(Manager, "thing", "other");

        var outcome = cli.Answer("n\n", "library", "remove", "thing");

        Assert.Equal(3, outcome.Exit);
        Assert.Contains($"Prefix '{where}' also holds other, which go with it.\n", outcome.Error);
    }

    [Fact]
    public void AnUninstallerCabinetCannotAttributeIsChosenByTheUser()
    {
        var where = Installed(Plugin, "thing", "other");
        File.WriteAllText(cli.Layout.PrefixSystemReg(where), """
            WINE REGISTRY Version 2

            [Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\One] 1787344290
            "DisplayName"="Thing version 1"
            "UninstallString"="C:\\one.exe"

            [Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\Two] 1787344290
            "DisplayName"="Thing Extras"
            "UninstallString"="C:\\two.exe"
            """);

        var outcome = cli.Answer("y\n\n", "library", "remove", "thing");

        Assert.Equal(3, outcome.Exit);
        Assert.EndsWith(
            "Any of these could be Thing's uninstaller:\n"
            + "  1  Thing version 1\n"
            + "  2  Thing Extras\n"
            + "Which one runs? [1-2, or Enter to leave it alone] Left alone.\n",
            outcome.Error);
        Assert.Empty(cli.Runner.Ran);
    }

    [Fact]
    public void AManagerRemovedWithYesTakesItsPrefix()
    {
        var where = Installed(Manager, "thing");

        var outcome = cli.Answer("y\n", "library", "remove", "thing");

        Assert.Equal(0, outcome.Exit);
        Assert.Contains("Thing and the prefix that held it are gone.", outcome.Out);
        Assert.False(Directory.Exists(cli.Layout.PrefixPath(where)));
    }

    [Fact]
    public void TheOnlyPluginInAPrefixOffersThePrefixAndThenItsUninstaller()
    {
        var where = Installed(Plugin, "thing");

        var outcome = cli.Answer("n\nn\n", "library", "remove", "thing");

        Assert.Equal(3, outcome.Exit);
        Assert.Equal(
            $"Thing is the only plugin Cabinet installed in prefix '{where}'.\n"
            + "Delete the prefix and everything in it? [y/N] "
            + "Run Thing's own uninstaller? It may open a window. [y/N] Left alone.\n",
            outcome.Error);
        Assert.Empty(cli.Runner.Ran);
    }

    [Fact]
    public void APluginSharingItsPrefixOffersOnlyItsUninstaller()
    {
        var where = Installed(Plugin, "thing", "other");

        var outcome = cli.Answer("n\n", "library", "remove", "thing");

        Assert.Equal(3, outcome.Exit);
        Assert.Equal(
            $"Prefix '{where}' also holds other, so it stays.\n"
            + "Run Thing's own uninstaller? It may open a window. [y/N] Left alone.\n",
            outcome.Error);
    }

    [Fact]
    public void ANativePluginAsksOnceAboutItsFilesAndLinks()
    {
        cli.Catalogue("synth", "Name: Synth\nKind: native\nSource: byo\n");
        Directory.CreateDirectory(cli.Layout.NativePath("synth"));

        var outcome = cli.Answer("n\n", "library", "remove", "synth");

        Assert.Equal(3, outcome.Exit);
        Assert.Equal("Remove Synth and the links your DAW scans? [y/N] Left alone.\n", outcome.Error);
        Assert.True(Directory.Exists(cli.Layout.NativePath("synth")));
    }

    [Fact]
    public void AnInstallTheCatalogueNoLongerListsIsListedAndRemovedByItsId()
    {
        cli.Catalogue("thing", Plugin);
        cli.Prefix("old", "gone");

        Assert.EndsWith(
            "No longer in the catalogue:\n"
            + "ok  gone   windows   in old\n\n"
            + "Remove one with `cabinet library remove <id>`.\n",
            cli.Run("library").Out);

        var outcome = cli.Answer("y\n", "library", "remove", "gone");

        Assert.Equal(0, outcome.Exit);
        Assert.False(Directory.Exists(cli.Layout.PrefixPath("old")));
        Assert.DoesNotContain("gone", cli.Run("library").Out);
    }

    [Fact]
    public void RemovingWhatIsNotInstalledIsAbsent()
    {
        cli.Catalogue("thing", Plugin);

        var outcome = cli.Run("library", "remove", "thing");

        Assert.Equal(4, outcome.Exit);
        Assert.Equal("cabinet: Thing is not installed\n", outcome.Error);
    }

    private string Installed(string text, params string[] recorded)
    {
        cli.Catalogue("thing", text);
        var where = LibraryEntry.Parse("thing", text).Prefix;
        cli.Prefix(where, recorded);
        return where;
    }
}
