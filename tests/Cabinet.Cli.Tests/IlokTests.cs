using System.Text.Json;
using Cabinet.Core;
using Cabinet.Core.Tests;

namespace Cabinet.Cli.Tests;

public sealed class IlokTests : IDisposable
{
    private readonly Cli cli;

    public IlokTests()
    {
        cli = new Cli(new RecordingRunner(IlokDownload.Serving, outputs: IlokDownload.Answering()));
        Directory.CreateDirectory(Path.Combine(cli.Layout.PrefixPath("gadget"), "dosdevices"));
    }

    public void Dispose() => cli.Dispose();

    [Fact]
    public void IlokNeedsAPrefixName()
    {
        var outcome = cli.Run("ilok");

        Assert.Equal(2, outcome.Exit);
        Assert.Equal("cabinet: expected a prefix name\n", outcome.Error);
    }

    [Fact]
    public void SettingIlokNeedsOnOrOff()
    {
        var outcome = cli.Run("set", "gadget", "ilok");

        Assert.Equal(2, outcome.Exit);
        Assert.Equal("cabinet: expected on or off\n", outcome.Error);
        Assert.Empty(cli.Runner.Ran);
    }

    [Fact]
    public void IlokInstallsTheLicenceServiceIntoThePrefix()
    {
        var outcome = cli.Run("ilok", "gadget");

        Assert.Equal(0, outcome.Exit);
        Assert.Contains($"gadget now licenses through iLok {IlokDownload.Version}.", outcome.Out);
        Assert.Single(cli.Runner.Ran, call => call.Arguments is ["msiexec", "/i", _, "/qn", "NODRIVER=1"]);
    }

    [Fact]
    public void ShowAndListSayWhichIlokAPrefixHas()
    {
        cli.Run("set", "gadget", "ilok", "on");

        var prefix = Assert.Single(Parsed.Objects(cli.Run("list", "--json").Out));

        Assert.Contains($"ilok              {IlokDownload.Version}", cli.Run("show", "gadget").Out);
        Assert.Contains($"ilok {IlokDownload.Version}", cli.Run("list").Out);
        Assert.Equal(IlokDownload.Version, prefix.GetProperty("ilok").GetString());
        Assert.Equal(JsonValueKind.Null, prefix.GetProperty("dxvk").ValueKind);
    }

    [Fact]
    public void SettingIlokOffUninstallsIt()
    {
        cli.Run("set", "gadget", "ilok", "on");

        var outcome = cli.Run("set", "gadget", "ilok", "off");

        Assert.Equal(0, outcome.Exit);
        Assert.Single(cli.Runner.Ran, call => call.Arguments is ["msiexec", "/x", Ilok.ProductCode, "/qn"]);
        Assert.Contains("ilok              off", cli.Run("show", "gadget").Out);
    }
}
