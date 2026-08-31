using Cabinet.Core;

namespace Cabinet.Core.Tests;

public sealed class ElfTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("cabinet").FullName;

    private string Library => Path.Combine(root, "plugin.so");

    [Fact]
    public void ARelinkWritesTheShorterNameOverTheLongerOneAndLeavesTheRestAlone()
    {
        File.WriteAllBytes(Library, SharedObject.Bytes());

        Assert.True(Elf.Relink(Library, SharedObject.FirstSoname, "libcurl.so.4"));

        var image = File.ReadAllBytes(Library);

        Assert.Equal("libcurl.so.4", SharedObject.Soname(image, SharedObject.First));
        Assert.Equal(new byte[7], image[(SharedObject.First + 12)..(SharedObject.First + 19)]);
        Assert.Equal(
            SharedObject.SecondSoname, SharedObject.Soname(image, SharedObject.Second));
    }

    [Fact]
    public void ASonameThatIsNotThereChangesNothing()
    {
        File.WriteAllBytes(Library, SharedObject.Bytes());

        Assert.False(Elf.Relink(Library, "libfoo.so.1", "libbar.so.1"));
        Assert.Equal(SharedObject.Bytes(), File.ReadAllBytes(Library));
    }

    [Fact]
    public void ARelinkToALongerNameIsRefusedBecauseTheStringTableCannotGrow()
    {
        File.WriteAllBytes(Library, SharedObject.Bytes());

        Assert.Throws<ArgumentException>(
            () => Elf.Relink(Library, SharedObject.SecondSoname, "libmath.so.6"));

        Assert.Equal(SharedObject.Bytes(), File.ReadAllBytes(Library));
    }

    [Fact]
    public void AFileThatIsNotASixtyFourBitElfIsLeftWhereItIs()
    {
        var manifest = Path.Combine(root, "manifest.ttl");
        File.WriteAllText(manifest, "@prefix lv2: <http://lv2plug.in/ns/lv2core#> .");

        Assert.False(Elf.Relink(manifest, SharedObject.FirstSoname, "libcurl.so.4"));
        Assert.Equal("@prefix lv2: <http://lv2plug.in/ns/lv2core#> .", File.ReadAllText(manifest));
    }

    public void Dispose() => Directory.Delete(root, recursive: true);
}
