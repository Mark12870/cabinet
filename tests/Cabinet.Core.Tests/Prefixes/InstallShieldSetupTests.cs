using Cabinet.Core;

namespace Cabinet.Core.Tests;

public sealed class InstallShieldSetupTests : IDisposable
{
    private readonly string root = TestRoot.Create("installshield");

    public void Dispose() => Directory.Delete(root, recursive: true);

    [Fact]
    public void AnEncodedFileComesOutAsItWentIn()
    {
        var content = SetupStreamWriter.Content(3000);
        var setup = Setup(SetupStreamWriter.Encoded("notes.txt", content));

        Assert.Equal(content, Extracted(setup, "notes.txt"));
    }

    [Fact]
    public void ADeflatedFileIsInflatedAfterDecoding()
    {
        var content = SetupStreamWriter.Content(5000);
        var setup = Setup(SetupStreamWriter.Deflated("package.msi", content));

        Assert.Equal(content, Extracted(setup, "package.msi"));
    }

    [Fact]
    public void AStoredFileIsCopiedAsItIs()
    {
        var content = SetupStreamWriter.Content(100);
        var setup = Setup(SetupStreamWriter.Plain("readme.txt", content));

        Assert.Equal(content, Extracted(setup, "readme.txt"));
    }

    [Fact]
    public void OnlyTheNamedFileIsTakenFromAmongTheOthers()
    {
        var wanted = SetupStreamWriter.Content(2048);
        var setup = Setup(
            SetupStreamWriter.Deflated("first.dll", SetupStreamWriter.Content(700)),
            SetupStreamWriter.Deflated("second package.msi", wanted),
            SetupStreamWriter.Encoded("third.ini", SetupStreamWriter.Content(10)));

        Assert.Equal(wanted, Extracted(setup, "second package.msi"));
    }

    [Fact]
    public void AFileTheSetupDoesNotCarryIsNamed()
    {
        var setup = Setup(SetupStreamWriter.Encoded("notes.txt", SetupStreamWriter.Content(10)));

        var missing = Assert.Throws<FileNotFoundException>(() => Extracted(setup, "package.msi"));

        Assert.Equal("setup.exe carries no package.msi", missing.Message);
    }

    [Fact]
    public void AnythingButAnInstallShieldSetupIsRefused()
    {
        var setup = Path.Combine(root, "setup.exe");
        File.WriteAllBytes(setup, SetupStreamWriter.Content(4096));

        var refused = Assert.Throws<InvalidDataException>(() => Extracted(setup, "package.msi"));

        Assert.Equal("setup.exe is not an InstallShield setup — it carries no ISSetupStream", refused.Message);
    }

    [Fact]
    public void ASetupCutShortIsRefused()
    {
        var whole = SetupStreamWriter.Setup(SetupStreamWriter.Encoded("notes.txt", SetupStreamWriter.Content(500)));
        var setup = Path.Combine(root, "setup.exe");
        File.WriteAllBytes(setup, whole[..^100]);

        Assert.Throws<InvalidDataException>(() => Extracted(setup, "notes.txt"));
    }

    private string Setup(params SetupStreamWriter.Stored[] files)
    {
        var setup = Path.Combine(root, "setup.exe");
        File.WriteAllBytes(setup, SetupStreamWriter.Setup(files));
        return setup;
    }

    private byte[] Extracted(string setup, string name)
    {
        var target = Path.Combine(root, "extracted");
        InstallShieldSetup.Extract(setup, name, target);
        return File.ReadAllBytes(target);
    }
}
