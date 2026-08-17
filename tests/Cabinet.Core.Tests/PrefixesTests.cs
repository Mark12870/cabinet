using Cabinet.Core;

namespace Cabinet.Core.Tests;

public sealed class PrefixesTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("cabinet").FullName;

    private Layout Layout => new(root, "/run/user/1000", Path.Combine(root, "data"));

    private Prefixes Subject => new(Layout, new UnusedRunner());

    [Fact]
    public void DeleteTakesThePrefixAndEverythingUnderIt()
    {
        var plugin = Path.Combine(
            Layout.PrefixPath("serum"), "drive_c", "Program Files", "Serum.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(plugin)!);
        File.WriteAllText(plugin, "");

        Subject.Delete("serum");

        Assert.False(Directory.Exists(Layout.PrefixPath("serum")));
    }

    [Fact]
    public void ANameThatWalksOutOfThePrefixesDirectoryIsRefused()
    {
        // Path.Combine would happily produce data/keep-me from a name containing `..`,
        // and this deletes recursively.
        var outsider = Path.Combine(root, "data", "keep-me");
        Directory.CreateDirectory(outsider);

        Assert.Throws<ArgumentException>(() => Subject.Delete("../keep-me"));
        Assert.True(Directory.Exists(outsider));
    }

    [Fact]
    public void DeletingAPrefixThatIsNotThereSaysSo()
    {
        Assert.Throws<DirectoryNotFoundException>(() => Subject.Delete("never-existed"));
    }

    public void Dispose() => Directory.Delete(root, recursive: true);
}
