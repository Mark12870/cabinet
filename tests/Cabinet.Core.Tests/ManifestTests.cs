using Cabinet.Core;

namespace Cabinet.Core.Tests;

public class ManifestTests
{
    private static readonly string[] Lines =
        Repo.Lines("io.github.mark12870.cabinet.yml");

    private static readonly HashSet<string> FinishArgs = Lines
        .Select(line => line.Trim())
        .Where(line => line.StartsWith("- --", StringComparison.Ordinal))
        .Select(line => line[2..].Split('#')[0].Trim())
        .ToHashSet(StringComparer.Ordinal);

    [Fact]
    public void TheAudioBuffersCanCrossTheBoundary()
    {
        Assert.Contains("--device=shm", FinishArgs);
    }

    [Fact]
    public void TheBaseIsTheWineThatStillRunsAThirtyTwoBitWinelibHost()
    {
        Assert.Equal("org.winehq.Wine", Field("base"));
        Assert.StartsWith("stable-", Field("base-version"));
    }

    [Fact]
    public void TheRuntimeIsTheOneThatCarriesGtkFourAndLibadwaita()
    {
        Assert.Equal("org.gnome.Platform", Field("runtime"));
        Assert.Equal("org.gnome.Sdk", Field("sdk"));
    }

    [Fact]
    public void TheExtensionsBaseDoesNotCopyAreDeclaredAgain()
    {
        string[] required =
        [
            "org.winehq.Wine.gecko",
            "org.winehq.Wine.mono",
            "org.freedesktop.Platform.Compat.i386",
            "org.freedesktop.Platform.GL32",
        ];

        var declared = Lines
            .Select(line => line.Trim().TrimEnd(':'))
            .ToHashSet(StringComparer.Ordinal);

        var undeclared = required.Where(name => !declared.Contains(name)).ToList();

        Assert.Empty(undeclared);
    }

    [Fact]
    public void EveryPathTheCodeReachesForIsGranted()
    {
        string[] required =
        [
            "--filesystem=~/.vst3:create",
            "--filesystem=~/.vst:create",
            "--filesystem=~/.clap:create",
            "--filesystem=~/.lv2:create",
            "--filesystem=~/.var/app",
            "--filesystem=~/.local/share/flatpak/overrides:ro",
            "--filesystem=~/.local/share/flatpak/repo/config:ro",
            "--filesystem=xdg-run/yabridge:create",
        ];

        var ungranted = required.Where(grant => !FinishArgs.Contains(grant)).ToList();

        Assert.Empty(ungranted);
    }

    [Fact]
    public void TheHomeGrantStaysReadOnly()
    {
        Assert.Contains("--filesystem=home:ro", FinishArgs);
        Assert.DoesNotContain("--filesystem=home", FinishArgs);
    }

    [Fact]
    public void TheCommandIsTheOneGnomeSoftwareFallsBackTo()
    {
        Assert.Equal("cabinet", Field("command"));
    }

    [Fact]
    public void TheGuiPublishDisablesSharedCompilation()
    {
        Assert.Contains(
            Lines,
            line => line.Contains("dotnet publish src/Cabinet.Gui", StringComparison.Ordinal)
                    && line.Contains("-p:UseSharedCompilation=false", StringComparison.Ordinal));
    }

    [Fact]
    public void CataloguePayloadArchivesAreInstalled()
    {
        Assert.Contains(
            Lines,
            line => line.Contains("for payload in \"$vendor\"*.zip", StringComparison.Ordinal));
    }

    [Fact]
    public void YabridgectlIsInstalledWithTheBridges()
    {
        Assert.Contains(
            Lines,
            line => line.Contains(
                "install -Dm755 yabridgectl-release/yabridgectl "
                + "${FLATPAK_DEST}/lib/yabridge/yabridgectl", StringComparison.Ordinal));
    }

    private static string Field(string key)
    {
        var prefix = key + ":";

        var line = Lines.FirstOrDefault(l => l.StartsWith(prefix, StringComparison.Ordinal))
                   ?? throw new InvalidOperationException($"the manifest has no {key}");

        return line[prefix.Length..].Split('#')[0].Trim().Trim('\'');
    }
}
