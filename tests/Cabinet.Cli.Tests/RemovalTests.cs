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

        Assert.Equal(1, outcome.Exit);
        Assert.Equal(
            $"Thing's own uninstaller leaves everything it downloaded in prefix '{where}', "
            + "so it is the prefix or nothing.\n"
            + $"Delete '{where}' and every library Thing put in it? [y/N] Left alone.\n",
            outcome.Out);
        Assert.True(Directory.Exists(cli.Layout.PrefixPath(where)));
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

        Assert.Equal(1, outcome.Exit);
        Assert.Equal(
            $"Thing is the only plugin Cabinet installed in prefix '{where}'.\n"
            + "Delete the prefix and everything in it? [y/N] "
            + "Run Thing's own uninstaller? It may open a window. [y/N] Left alone.\n",
            outcome.Out);
        Assert.Empty(cli.Runner.Ran);
    }

    [Fact]
    public void APluginSharingItsPrefixOffersOnlyItsUninstaller()
    {
        var where = Installed(Plugin, "thing", "other");

        var outcome = cli.Answer("n\n", "library", "remove", "thing");

        Assert.Equal(1, outcome.Exit);
        Assert.Equal(
            $"Prefix '{where}' also holds other, so it stays.\n"
            + "Run Thing's own uninstaller? It may open a window. [y/N] Left alone.\n",
            outcome.Out);
    }

    [Fact]
    public void ANativePluginAsksOnceAboutItsFilesAndLinks()
    {
        cli.Catalogue("synth", "Name: Synth\nKind: native\nSource: byo\n");
        Directory.CreateDirectory(cli.Layout.NativePath("synth"));

        var outcome = cli.Answer("n\n", "library", "remove", "synth");

        Assert.Equal(1, outcome.Exit);
        Assert.Equal("Remove Synth and the links your DAW scans? [y/N] Left alone.\n", outcome.Out);
        Assert.True(Directory.Exists(cli.Layout.NativePath("synth")));
    }

    [Fact]
    public void RemovingWhatIsNotInstalledFails()
    {
        cli.Catalogue("thing", Plugin);

        var outcome = cli.Run("library", "remove", "thing");

        Assert.Equal(1, outcome.Exit);
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
