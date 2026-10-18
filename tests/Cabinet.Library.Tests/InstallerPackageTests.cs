using Cabinet.Core;

namespace Cabinet.Core.Tests;

public sealed class InstallerPackageTests : IDisposable
{
    private const string Payload = @"C:\payload\data\";

    private readonly string root = TestRoot.Create("installer-package");

    private string Package => Path.Combine(root, "Synth Setup PC.msi");

    internal static PackageWriter Synth(string synthCondition = "A1 = \"TRUE\"") =>
        new PackageWriter()
            .Table(
                "Property",
                ["Property:s", "Value:s"],
                ["P1_1", @"C:\Program Files\Vendor\Synth"],
                ["P2_1", @"C:\Program Files\Common Files\Vendor"],
                ["A1", "TRUE"],
                ["A2", "TRUE"],
                ["A3", "FALSE"])
            .Table(
                "Directory",
                ["Directory:s", "Directory_Parent:s", "DefaultDir:s"],
                ["TARGETDIR", null, "SourceDir"],
                ["P1_1", "TARGETDIR", "."],
                ["P1_1Offline", "P1_1", ".:OFFLIN~1|OFFLINE"],
                ["P1_1Group", "P1_1Offline", ".:AAAA"],
                ["P2_1", "TARGETDIR", "."],
                ["P2_1Synth", "P2_1", "Synth:."],
                ["P2_1Presets", "P2_1Synth", "Preset~1|Presets:BBBB"],
                ["P3_1", null, "."])
            .Table(
                "Component",
                ["Component:s", "Directory_:s", "Condition:s"],
                ["CSynth", "P1_1Group", synthCondition],
                ["CPresets", "P2_1Presets", "A2 = \"TRUE\""],
                ["CAax", "P3_1", "A3 = \"TRUE\""],
                ["CRegistry", "P1_1", null])
            .Table(
                "File",
                ["File:s", "Component_:s", "FileName:s"],
                ["FSynth", "CSynth", "Synth~1.exe|Synth.exe"],
                ["FPreset", "CPresets", "Default.nkm"],
                ["FAax", "CAax", "Synth.aaxplugin"])
            .Table(
                "Registry",
                ["Registry:s", "Root:i2", "Key:s", "Name:s", "Value:s", "Component_:s"],
                ["RInstall", 2, @"SOFTWARE\Vendor\Synth", "InstallDir", "[P1_1]", "CRegistry"],
                ["RBits", 2, @"SOFTWARE\Vendor\Synth", "Bits", "#64", "CRegistry"],
                ["RHash", 2, @"SOFTWARE\Vendor\Synth", "Tag", "##1", "CRegistry"],
                ["RType", 0, ".syn", null, "Vendor.Synth [\\[]1[\\]]", "CRegistry"],
                ["RKey", 1, @"Software\Vendor\Synth", "*", null, "CRegistry"],
                ["RAax", -1, @"SOFTWARE\Vendor\Aax", "Dir", "[P3_1]", "CAax"]);

    [Fact]
    public void EachFileGoesWhereThePackagesPropertiesPointAndComesFromTheSourceLayout()
    {
        Synth().Save(Package);

        var plan = InstallerPackage.Read(Package).Plan(new Dictionary<string, string>(), Payload);

        Assert.Equal(
            [
                new PackagedFile(@"C:\payload\data\OFFLINE\AAAA\Synth.exe", @"C:\Program Files\Vendor\Synth\Synth.exe"),
                new PackagedFile(
                    @"C:\payload\data\BBBB\Default.nkm", @"C:\Program Files\Common Files\Vendor\Synth\Presets\Default.nkm"),
            ],
            plan.Files);
    }

    [Fact]
    public void TheCommandLineOverridesThePackagesOwnProperties()
    {
        Synth().Save(Package);

        var plan = InstallerPackage.Read(Package).Plan(
            InstallerPackage.CommandLine(@"A1=FALSE A3=TRUE P3_1=""C:\Program Files\Common Files\Avid"""), Payload);

        Assert.Equal(
            [
                new PackagedFile(
                    @"C:\payload\data\BBBB\Default.nkm", @"C:\Program Files\Common Files\Vendor\Synth\Presets\Default.nkm"),
                new PackagedFile(@"C:\payload\data\Synth.aaxplugin", @"C:\Program Files\Common Files\Avid\Synth.aaxplugin"),
            ],
            plan.Files);
    }

    [Fact]
    public void RegistryValuesOfInstalledComponentsAreFormattedFromProperties()
    {
        Synth().Save(Package);

        var plan = InstallerPackage.Read(Package).Plan(new Dictionary<string, string>(), Payload);

        Assert.Equal(
            [
                new PackagedValue(
                    @"HKEY_LOCAL_MACHINE\SOFTWARE\Vendor\Synth", "InstallDir", @"C:\Program Files\Vendor\Synth\", null),
                new PackagedValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Vendor\Synth", "Bits", null, 64),
                new PackagedValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Vendor\Synth", "Tag", "#1", null),
                new PackagedValue(@"HKEY_CLASSES_ROOT\.syn", null, "Vendor.Synth [1]", null),
                new PackagedValue(@"HKEY_CURRENT_USER\Software\Vendor\Synth", null, null, null),
            ],
            plan.Values);
    }

    [Fact]
    public void WindowsOwnFoldersPlaceFilesNoPropertyMentions()
    {
        new PackageWriter()
            .Table(
                "Directory",
                ["Directory:s", "Directory_Parent:s", "DefaultDir:s"],
                ["TARGETDIR", null, "SourceDir"],
                ["ProgramFiles64Folder", "TARGETDIR", "."],
                ["CommonFiles64Folder", "ProgramFiles64Folder", "."],
                ["Vst3", "CommonFiles64Folder", "VST3:."])
            .Table("Component", ["Component:s", "Directory_:s", "Condition:s"], ["CPlugin", "Vst3", null])
            .Table("File", ["File:s", "Component_:s", "FileName:s"], ["FPlugin", "CPlugin", "Synth.vst3"])
            .Save(Package);

        var plan = InstallerPackage.Read(Package).Plan(new Dictionary<string, string>(), Payload);

        Assert.Equal(
            [new PackagedFile(@"C:\payload\data\Synth.vst3", @"C:\Program Files\Common Files\VST3\Synth.vst3")],
            plan.Files);
    }

    [Fact]
    public void ASelectionOfFeaturesIsRefusedRatherThanIgnored()
    {
        Synth().Save(Package);

        var refused = Assert.Throws<NotSupportedException>(
            () => InstallerPackage.Read(Package).Plan(InstallerPackage.CommandLine("ADDLOCAL=Vst3,Standalone"), Payload));

        Assert.Equal("Cabinet installs every feature of a package, not ADDLOCAL=Vst3,Standalone", refused.Message);
    }

    [Fact]
    public void ACommandLineReadsBareAndQuotedProperties() =>
        Assert.Equal(
            new Dictionary<string, string>
            {
                ["ADDLOCAL"] = "ALL",
                ["ARPINSTALLLOCATION"] = @"C:\Program Files\Native Instruments\Kontakt 8",
                ["SRCDIR"] = @"C:\users\someone\AppData\Local\Temp\mia1.tmp\data\",
                ["SAID"] = "say \"hi\"",
            },
            InstallerPackage.CommandLine(
                @"ADDLOCAL=ALL ARPINSTALLLOCATION=""C:\Program Files\Native Instruments\Kontakt 8"" "
                + @"SRCDIR=C:\users\someone\AppData\Local\Temp\mia1.tmp\data\ SAID=""say """"hi"""""""));

    [Fact]
    public void AConditionCabinetCannotEvaluateIsRefusedRatherThanGuessed()
    {
        Synth("A1 OR A2").Save(Package);

        var refused = Assert.Throws<NotSupportedException>(
            () => InstallerPackage.Read(Package).Plan(new Dictionary<string, string>(), Payload));

        Assert.Equal("Cabinet understands only Property = \"Value\" component conditions, not A1 OR A2", refused.Message);
    }

    [Fact]
    public void AnInstalledFileWithNowhereToGoIsRefused()
    {
        Synth().Save(Package);

        var refused = Assert.Throws<InvalidDataException>(
            () => InstallerPackage.Read(Package).Plan(new Dictionary<string, string> { ["A3"] = "TRUE" }, Payload));

        Assert.Equal("nothing says where Synth.aaxplugin goes: the package leaves P3_1 unset", refused.Message);
    }

    [Fact]
    public void ATableLargerThanTheMiniStreamIsReadFromFullSectors()
    {
        var files = Enumerable.Range(0, 1500).Select(index => new object?[] { $"F{index}", "CPresets", $"{index}.nkm" });
        new PackageWriter()
            .Table("Property", ["Property:s", "Value:s"], ["P2_1", @"C:\Vendor"])
            .Table(
                "Directory",
                ["Directory:s", "Directory_Parent:s", "DefaultDir:s"],
                ["TARGETDIR", null, "SourceDir"],
                ["P2_1", "TARGETDIR", "."])
            .Table("Component", ["Component:s", "Directory_:s", "Condition:s"], ["CPresets", "P2_1", null])
            .Table("File", ["File:s", "Component_:s", "FileName:s"], [.. files])
            .Save(Package);

        var plan = InstallerPackage.Read(Package).Plan(new Dictionary<string, string>(), Payload);

        Assert.Equal(1500, plan.Files.Count);
        Assert.Equal(new PackagedFile(@"C:\payload\data\1499.nkm", @"C:\Vendor\1499.nkm"), plan.Files[^1]);
    }

    [Fact]
    public void TablesLeftWithNarrowReferencesAfterThePoolGrewWideAreStillRead()
    {
        new PackageWriter()
            .Table(
                "Directory",
                ["Directory:s", "Directory_Parent:s", "DefaultDir:s"],
                ["TARGETDIR", null, "SourceDir"],
                ["P1_1", "TARGETDIR", "."])
            .Table("Component", ["Component:s", "Directory_:s", "Condition:s"], ["CSynth", "P1_1", "A1 = \"TRUE\""])
            .Table("File", ["File:s", "Component_:s", "FileName:s"], ["FSynth", "CSynth", "Synth.exe"])
            .WidePool()
            .Table("Property", ["Property:s", "Value:s"], ["P1_1", @"C:\Program Files\Vendor\Synth"], ["A1", "TRUE"])
            .Save(Package);

        var plan = InstallerPackage.Read(Package).Plan(new Dictionary<string, string>(), Payload);

        Assert.Equal(
            [new PackagedFile(@"C:\payload\data\Synth.exe", @"C:\Program Files\Vendor\Synth\Synth.exe")], plan.Files);
    }

    [Fact]
    public void AFileThatIsNoPackageIsRefused()
    {
        File.WriteAllText(Package, "MZ not a package");

        var refused = Assert.Throws<InvalidDataException>(() => InstallerPackage.Read(Package));

        Assert.Equal("not a Windows Installer package: no compound file header", refused.Message);
    }

    public void Dispose() => Directory.Delete(root, recursive: true);
}
