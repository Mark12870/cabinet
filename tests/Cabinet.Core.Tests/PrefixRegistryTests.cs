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
        User(Gadget);

        var entry = Assert.Single(Subject.Uninstallers("valhalla"));

        Assert.Equal("Acme Gadget 2", entry.Name);
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
        User(Gadget);

        Assert.Equal(
            ["Acme Gadget 2", "FabFilter Total Bundle", "ValhallaSupermassive version 5.0.0"],
            Subject.Uninstallers("valhalla").Select(one => one.Name).Order());
    }

    [Fact]
    public void AnMsiWritesItsCommandWithWinesTypePrefix()
    {
        System(Sitala);

        Assert.Equal(
            "MsiExec.exe /I{74B609F8-3755-424B-BC0F-71581EDB4123}",
            Assert.Single(Subject.Uninstallers("valhalla")).Command);
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

    private const string Sitala = """
        [Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\{74B609F8-3755-424B-BC0F-71581EDB4123}] 1787417846
        "DisplayName"="Sitala"
        "DisplayVersion"="1.0.9"
        "EstimatedSize"=dword:00000000
        "ModifyPath"=str(2):"MsiExec.exe /I{74B609F8-3755-424B-BC0F-71581EDB4123}"
        "UninstallString"=str(2):"MsiExec.exe /I{74B609F8-3755-424B-BC0F-71581EDB4123}"
        """;

    private const string FabFilter = """
        [Software\\Wow6432Node\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\FabFilter Total Bundle] 1787254785
        "DisplayName"="FabFilter Total Bundle"
        "UninstallString"="C:\\Program Files\\FabFilter\\Uninst.exe"
        """;

    private const string Gadget = """
        [Software\\Wow6432Node\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\Gadget2] 1787344290
        "DisplayName"="Acme Gadget 2"
        "UninstallString"="\"C:\\users\\testuser\\AppData\\Local\\Acme\\Uninstall_Gadget2.exe\""
        """;

    [Fact]
    public void AValueIsFoundUnderTheKeyThatHoldsIt()
    {
        User("""
             [Software\\Wine\\Explorer\\Desktops] 1787567817
             "Default"="1920x1080"

             [Software\\Wine\\Explorer] 1787567817
             "Desktop"="Default"
             """);

        Assert.Equal("Default", Subject.Lookup("valhalla", @"Software\Wine\Explorer", "Desktop"));
        Assert.Equal(
            "1920x1080",
            Subject.Lookup("valhalla", @"Software\Wine\Explorer\Desktops", "Default"));
    }

    [Fact]
    public void AValueIsNotFoundUnderSomeOtherKey()
    {
        User("""
             [Software\\Wine\\Explorer\\Desktops] 1787567817
             "Default"="1920x1080"
             """);

        Assert.Null(Subject.Lookup("valhalla", @"Software\Wine\Explorer", "Desktop"));
        Assert.Null(Subject.Lookup("valhalla", @"Software\Wine\Explorer\Desktops", "Other"));
    }

    private void System(string text) => File.WriteAllText(
        Layout.PrefixSystemReg("valhalla"),
        "WINE REGISTRY Version 2\n;; All keys relative to REGISTRY\\\\Machine\n\n" + text + "\n");

    private void User(string text) => File.WriteAllText(
        Layout.PrefixUserReg("valhalla"),
        "WINE REGISTRY Version 2\n;; All keys relative to REGISTRY\\\\User\\\\S-1-5-21\n\n"
        + text + "\n");

    public void Dispose() => Directory.Delete(root, recursive: true);
}
