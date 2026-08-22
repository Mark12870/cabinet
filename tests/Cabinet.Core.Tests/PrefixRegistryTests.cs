using Cabinet.Core;

namespace Cabinet.Core.Tests;

public sealed class PrefixRegistryTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("cabinet").FullName;

    private Layout Layout => new(root, "/run/user/1000", Path.Combine(root, "data"));

    private PrefixRegistry Subject => new(Layout);

    public PrefixRegistryTests() => Directory.CreateDirectory(Layout.PrefixPath("valhalla"));

    [Fact]
    public void APrefixWithNoRegistryRegistersNothing()
    {
        Assert.Empty(Subject.Uninstallers("valhalla"));
    }

    [Fact]
    public void AQuietUninstallStringWinsOverTheOneThatOpensAWindow()
    {
        System(Valhalla);

        var entry = Assert.Single(Subject.Uninstallers("valhalla"));

        Assert.Equal("ValhallaSupermassive version 5.0.0", entry.Name);
        Assert.Equal(
            @"""C:\ProgramData\Valhalla DSP, LLC\ValhallaSupermassive\InstallerFiles\unins000.exe"" /SILENT",
            entry.Command);
        Assert.Equal(
            @"HKLM\Software\Wow6432Node\Microsoft\Windows\CurrentVersion\Uninstall\{83D52E63-9A59-40A1-9117-99A257CBC189}_is1",
            entry.Key);
    }

    [Fact]
    public void AnUnquotedPathWithASpaceInItIsKeptWhole()
    {
        System(FabFilter);

        Assert.Equal(
            @"C:\Program Files\FabFilter\Uninst.exe",
            Assert.Single(Subject.Uninstallers("valhalla")).Command);
    }

    [Fact]
    public void UserRegistrationsAreReadToo()
    {
        User(Serum);

        var entry = Assert.Single(Subject.Uninstallers("valhalla"));

        Assert.Equal("Xfer Records Serum 2", entry.Name);
        Assert.StartsWith(@"HKCU\", entry.Key, StringComparison.Ordinal);
    }

    [Fact]
    public void SomethingThatIsNotAnUninstallerIsNotOne()
    {
        System("""
               [Software\\Wow6432Node\\Microsoft\\Windows\\CurrentVersion\\Run] 1787390229
               "DisplayName"="Not an uninstaller"
               "UninstallString"="C:\\nope.exe"

               [Software\\Classes\\CLSID\\{1234}] 1787390229
               "DisplayName"="Also not"
               """);

        Assert.Empty(Subject.Uninstallers("valhalla"));
    }

    [Fact]
    public void AnEntryWithNothingToRunIsSkipped()
    {
        System("""
               [Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\Wine Mono Runtime] 1787390229
               "DisplayName"="Wine Mono Runtime"
               "NoModify"=dword:00000001
               """);

        Assert.Empty(Subject.Uninstallers("valhalla"));
    }

    [Fact]
    public void ASubkeyOfAnUninstallEntryIsNotAnEntryOfItsOwn()
    {
        System("""
               [Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\Thing\\Extra] 1787390229
               "UninstallString"="C:\\thing.exe"
               """);

        Assert.Empty(Subject.Uninstallers("valhalla"));
    }

    [Fact]
    public void EveryProductInTheSamePrefixIsListed()
    {
        System(Valhalla + "\n" + FabFilter);
        User(Serum);

        Assert.Equal(
            ["FabFilter Total Bundle", "ValhallaSupermassive version 5.0.0", "Xfer Records Serum 2"],
            Subject.Uninstallers("valhalla").Select(one => one.Name).Order());
    }

    private const string Valhalla = """
        [Software\\Wow6432Node\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\{83D52E63-9A59-40A1-9117-99A257CBC189}_is1] 1787390242
        #time=1dd321729b5cb96
        "DisplayName"="ValhallaSupermassive version 5.0.0"
        "EstimatedSize"=dword:00003159
        "Inno Setup: App Path"="C:\\ProgramData\\Valhalla DSP, LLC\\ValhallaSupermassive\\InstallerFiles"
        "QuietUninstallString"="\"C:\\ProgramData\\Valhalla DSP, LLC\\ValhallaSupermassive\\InstallerFiles\\unins000.exe\" /SILENT"
        "UninstallString"="\"C:\\ProgramData\\Valhalla DSP, LLC\\ValhallaSupermassive\\InstallerFiles\\unins000.exe\""
        """;

    private const string FabFilter = """
        [Software\\Wow6432Node\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\FabFilter Total Bundle] 1787254785
        "DisplayName"="FabFilter Total Bundle"
        "UninstallString"="C:\\Program Files\\FabFilter\\Uninst.exe"
        """;

    private const string Serum = """
        [Software\\Wow6432Node\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\Serum2] 1787344290
        "DisplayName"="Xfer Records Serum 2"
        "UninstallString"="\"C:\\users\\testuser\\AppData\\Local\\Xfer\\Uninstall_Serum2.exe\""
        """;

    private void System(string text) => File.WriteAllText(
        Layout.PrefixSystemReg("valhalla"),
        "WINE REGISTRY Version 2\n;; All keys relative to REGISTRY\\\\Machine\n\n" + text + "\n");

    private void User(string text) => File.WriteAllText(
        Layout.PrefixUserReg("valhalla"),
        "WINE REGISTRY Version 2\n;; All keys relative to REGISTRY\\\\User\\\\S-1-5-21\n\n"
        + text + "\n");

    public void Dispose() => Directory.Delete(root, recursive: true);
}
