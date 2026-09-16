using System.Xml.Linq;

using Cabinet.Core;

namespace Cabinet.Core.Tests;

public class ManifestTests
{
    private static readonly string[] Lines =
        Repo.Lines("io.github.mark12870.cabinet.yml");

    private static readonly XDocument WindowSchema =
        XDocument.Load(Repo.Path("data/io.github.mark12870.cabinet.gschema.xml"));

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
    public void TheWrapperFallsBackToTheShimAndNotToABareWine()
    {
        var text = string.Join('\n', Lines);

        Assert.Contains(
            """sed -i 's|WINELOADER="wine"|WINELOADER="$appdir/cabinet-wine"|'""", text);
        Assert.Contains("""grep -q 'WINELOADER="$appdir/cabinet-wine"'""", text);
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
    public void WinetricksGuiBackendIsPackaged()
    {
        Assert.Contains(Lines, line => line.Trim() == "- name: zenity");
        Assert.Contains(Lines, line => line.Trim() == "- -Dmanpage=false");
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
            "--filesystem=~/.local/share/yabridge:create",
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
    public void WindowSettingsAreCompiledIntoTheApplication()
    {
        var schema = WindowSchema.Root!.Element("schema")!;
        var keys = schema.Elements("key").ToDictionary(
            key => key.Attribute("name")!.Value,
            StringComparer.Ordinal);

        Assert.Equal("io.github.mark12870.cabinet", schema.Attribute("id")!.Value);
        Assert.Equal("i", keys["window-width"].Attribute("type")!.Value);
        Assert.Equal("1100", keys["window-width"].Element("default")!.Value.Trim());
        Assert.Equal("i", keys["window-height"].Attribute("type")!.Value);
        Assert.Equal("760", keys["window-height"].Element("default")!.Value.Trim());

        var install = Array.FindIndex(
            Lines,
            line => line.Contains(
                "install -Dm644 data/${FLATPAK_ID}.gschema.xml", StringComparison.Ordinal));
        var compile = Array.FindIndex(
            Lines,
            line => line.Trim()
                == "- glib-compile-schemas ${FLATPAK_DEST}/share/glib-2.0/schemas");

        Assert.True(install >= 0);
        Assert.Equal(
            "${FLATPAK_DEST}/share/glib-2.0/schemas/${FLATPAK_ID}.gschema.xml",
            Lines[install + 1].Trim());
        Assert.True(compile > install);
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

    [Fact]
    public void APluginThatCrashesInItsOwnTeardownDoesNotTakeTheHostWithIt()
    {
        const string patch = "patches/yabridge-teardown-guard.patch";
        var archive = Array.FindIndex(
            Lines,
            line => line.Contains("github.com/robbert-vdh/yabridge/archive/", StringComparison.Ordinal));
        var applied = Array.FindIndex(Lines, line => line.Trim() == "path: " + patch);
        var guard = File.ReadAllText(Repo.Path(patch));

        Assert.True(archive >= 0);
        Assert.Equal(archive + 4, applied);
        Assert.Contains("AddVectoredExceptionHandler", guard, StringComparison.Ordinal);
        Assert.Contains("survive_access_violation([doomed]", guard, StringComparison.Ordinal);
        Assert.Contains("survive_access_violation([view]", guard, StringComparison.Ordinal);
    }

    [Fact]
    public void APluginCannotRevokeDragAndDropOnAWindowOfAnotherProcess()
    {
        const string patch = "patches/yabridge-foreign-drag-drop.patch";
        var guard = Array.FindIndex(
            Lines,
            line => line.Trim() == "path: patches/yabridge-teardown-guard.patch");
        var applied = Array.FindIndex(Lines, line => line.Trim() == "path: " + patch);
        var redirect = File.ReadAllText(Repo.Path(patch));

        Assert.True(guard >= 0);
        Assert.Equal(guard + 2, applied);
        Assert.Contains("GetWindowThreadProcessId(window, &owner)", redirect, StringComparison.Ordinal);
        Assert.Contains("return DRAGDROP_E_INVALIDHWND;", redirect, StringComparison.Ordinal);
        Assert.Contains("+        redirect_foreign_drag_drop_revocations();", redirect, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSocketsLandWhereTheSandboxCanReachThem()
    {
        Assert.Contains("--filesystem=xdg-run/yabridge:create", FinishArgs);
    }

    [Fact]
    public void TheYabridgeItBuildsIsPinnedAndExplained()
    {
        var documented = File.ReadAllText(Repo.Path("PATCHES.MD"));
        var source = Lines.Single(line =>
            line.Contains("robbert-vdh/yabridge/archive/", StringComparison.Ordinal));
        var reference = source
            .Split('/')[^1]
            .Replace(".tar.gz", "", StringComparison.Ordinal)
            .Trim();

        Assert.Contains($"## {reference}", documented, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryYabridgePatchIsAppliedAndDocumented()
    {
        var documented = File.ReadAllText(Repo.Path("PATCHES.MD"));
        var patches = Directory.GetFiles(Repo.Path("patches"), "*.patch")
            .Select(Path.GetFileName)
            .ToList();

        Assert.NotEmpty(patches);
        Assert.All(patches, patch => Assert.Contains(Lines, line => line.Trim() == $"path: patches/{patch}"));
        Assert.All(patches, patch => Assert.Contains($"## {patch}", documented, StringComparison.Ordinal));
    }

    private static string Field(string key)
    {
        var prefix = key + ":";

        var line = Lines.FirstOrDefault(l => l.StartsWith(prefix, StringComparison.Ordinal))
                   ?? throw new InvalidOperationException($"the manifest has no {key}");

        return line[prefix.Length..].Split('#')[0].Trim().Trim('\'');
    }
}
