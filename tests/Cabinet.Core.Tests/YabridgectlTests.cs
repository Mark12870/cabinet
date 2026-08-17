using Cabinet.Core;

namespace Cabinet.Core.Tests;

public class YabridgectlTests
{
    private const string Prefixes =
        "/home/u/.var/app/io.github.mark12870.cabinet/data/prefixes";

    private static Yabridgectl Subject =>
        new(new Layout("/home/u", "/run/user/1000"), new UnusedRunner());

    [Fact]
    public void ADeletedPrefixIsUnregistered()
    {
        var stale = Subject.StaleRegistrations(
            [
                $"{Prefixes}/gone/drive_c/Program Files/VstPlugins",
                $"{Prefixes}/kept/drive_c/Program Files/VstPlugins",
            ],
            new HashSet<string> { $"{Prefixes}/kept/drive_c/Program Files/VstPlugins" });

        Assert.Equal([$"{Prefixes}/gone/drive_c/Program Files/VstPlugins"], stale);
    }

    [Fact]
    public void ADirectoryAddedByHandIsLeftAlone()
    {
        // Outside the prefixes directory, so not Cabinet's to unregister even though
        // nothing wants it.
        var stale = Subject.StaleRegistrations(
            ["/home/u/.wine/drive_c/Program Files/VstPlugins"], new HashSet<string>());

        Assert.Empty(stale);
    }

    [Fact]
    public void ASiblingOfThePrefixesDirectoryIsNotMistakenForOne()
    {
        var stale = Subject.StaleRegistrations(
            [$"{Prefixes}-backup/gone/drive_c/Program Files/VstPlugins"],
            new HashSet<string>());

        Assert.Empty(stale);
    }
}
