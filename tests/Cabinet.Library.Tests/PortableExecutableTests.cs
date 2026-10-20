using Cabinet.Core;

namespace Cabinet.Core.Tests;

public sealed class PortableExecutableTests : IDisposable
{
    private readonly string root = TestRoot.Create("pe");

    private string Program => Path.Combine(root, "Thing.exe");

    [Fact]
    public void AnExecutableReportsTheProductVersionInItsVersionResource()
    {
        File.WriteAllBytes(Program, Executable.Bytes(17, 1, 3, 9270));

        Assert.Equal("17.1.3.9270", PortableExecutable.Version(Program));
    }

    [Fact]
    public void AnAllZeroVersionIsNoVersion()
    {
        File.WriteAllBytes(Program, Executable.Bytes(0, 0, 0, 0));

        Assert.Null(PortableExecutable.Version(Program));
    }

    [Fact]
    public void ATruncatedExecutableHasNoVersion()
    {
        File.WriteAllBytes(Program, Executable.Bytes(17, 1, 3, 9270)[..0x250]);

        Assert.Null(PortableExecutable.Version(Program));
    }

    [Fact]
    public void AThirtyTwoBitExecutableReportsItsProductVersion()
    {
        File.WriteAllBytes(Program, Executable.ThirtyTwoBitBytes(4, 2, 0, 11));

        Assert.Equal("4.2.0.11", PortableExecutable.Version(Program));
    }

    [Fact]
    public void AnExecutableWithoutResourcesHasNoVersion()
    {
        File.WriteAllBytes(Program, Executable.WithoutResources());

        Assert.Null(PortableExecutable.Version(Program));
    }

    [Fact]
    public void AnExecutableWhoseResourcesHoldNoVersionHasNoVersion()
    {
        File.WriteAllBytes(Program, Executable.WithoutVersionResource());

        Assert.Null(PortableExecutable.Version(Program));
    }

    [Fact]
    public void AFileThatIsNotAnExecutableHasNoVersion()
    {
        File.WriteAllText(Program, "@echo off");

        Assert.Null(PortableExecutable.Version(Program));
    }

    [Fact]
    public void AMissingFileHasNoVersion() =>
        Assert.Null(PortableExecutable.Version(Program));

    public void Dispose() => Directory.Delete(root, recursive: true);
}
