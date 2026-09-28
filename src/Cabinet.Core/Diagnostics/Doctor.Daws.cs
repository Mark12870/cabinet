namespace Cabinet.Core;

public sealed partial class Doctor
{
    public IReadOnlyList<string> DawsMissingPermissions() =>
        EnrolledDawIds().Where(dawId => EnrolledDaw(dawId).Status != Status.Ok).ToList();

    private IEnumerable<Check> EnrolledDaws() => EnrolledDawIds().Select(EnrolledDaw);

    private IEnumerable<string> EnrolledDawIds()
    {
        if (!Directory.Exists(layout.FlatpakOverridesDir))
        {
            return [];
        }

        return Directory.EnumerateFiles(layout.FlatpakOverridesDir)
            .Order(StringComparer.Ordinal)
            .Select(Path.GetFileName)
            .OfType<string>()
            .Where(dawId => dawId != Layout.AppId && Enrolment.IsAppId(dawId))
            .Where(dawId => ReadOverride(dawId)?.Get("Environment", "WINELOADER") is { } loader
                            && loader.Contains(Layout.AppId, StringComparison.Ordinal));
    }

    private IniFile? ReadOverride(string dawId)
    {
        try
        {
            return IniFile.Parse(File.ReadAllLines(layout.FlatpakOverride(dawId)));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private IEnumerable<Check> NativeDaw()
    {
        var entries = Layout.BridgedScanDirectories
            .Select(directory => (Link: layout.WindowsScanDir(directory),
                Target: layout.BridgeOutputDir(directory)))
            .ToList();
        var owned = Enrolment.ScanLinkConflicts(layout);
        var unlinked = entries
            .Where(entry => new DirectoryInfo(entry.Link).LinkTarget != entry.Target)
            .Select(entry => entry.Link)
            .ToList();

        if (owned.Count > 0)
        {
            yield return new Check("native DAWs", Status.Fail,
                $"already owned by something else: {string.Join(", ", owned)} "
                + "— move it aside and bridge what is installed again");
            yield break;
        }

        if (unlinked.Count > 0)
        {
            yield return new Check("native DAWs", Status.Fail,
                $"missing Cabinet scan paths: {string.Join(", ", unlinked)} "
                + "— bridge what is installed again");
            yield break;
        }

        var hasPlugins = entries.Any(entry => Directory.Exists(entry.Target)
            && Directory.EnumerateFiles(entry.Target, "*", SearchOption.AllDirectories)
                .Any(file => Path.GetExtension(file) is ".so" or ".clap"));

        yield return hasPlugins
            ? new Check("native DAWs", Status.Ok,
                $"Cabinet plugins are under {string.Join(", ", entries.Select(e => e.Link))}; "
                + $"independent yabridge remains at {layout.NativeYabridgeDir}")
            : new Check("native DAWs", Status.Fail,
                "no Cabinet native plugins have been published — bridge what is installed again");
    }

    private Check EnrolledDaw(string dawId)
    {
        if (ReadOverride(dawId) is not { } ini)
        {
            return new Check($"DAW {dawId}", Status.Fail, $"cannot read {layout.FlatpakOverride(dawId)}");
        }

        var missing = new List<string>();

        foreach (var argument in Enrolment.OverrideArguments(dawId, layout))
        {
            if (argument.StartsWith("--device=", StringComparison.Ordinal))
            {
                var device = argument["--device=".Length..];
                if (!Values(ini.Get("Context", "devices")).Contains(device, StringComparer.Ordinal))
                {
                    missing.Add(argument);
                }
            }
            else if (argument.StartsWith("--filesystem=", StringComparison.Ordinal))
            {
                var filesystem = argument["--filesystem=".Length..];
                if (!HasFilesystem(ini.Get("Context", "filesystems"), filesystem))
                {
                    missing.Add(argument);
                }
            }
            else if (argument.StartsWith("--talk-name=", StringComparison.Ordinal))
            {
                var name = argument["--talk-name=".Length..];
                if (ini.Get("Session Bus Policy", name) != "talk")
                {
                    missing.Add(argument);
                }
            }
            else if (argument.StartsWith("--env=", StringComparison.Ordinal))
            {
                var assignment = argument["--env=".Length..].Split('=', 2);
                if (ini.Get("Environment", assignment[0]) != assignment[^1])
                {
                    missing.Add(argument);
                }
            }
        }

        return missing.Count == 0
            ? new Check($"DAW {dawId}", Status.Ok, "enrolled — " + Enrolment.TrustBoundary(dawId))
            : new Check($"DAW {dawId}", Status.Fail, "missing " + string.Join(", ", missing));
    }

    private static IEnumerable<string> Values(string? value) =>
        value?.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];

    private static bool HasFilesystem(string? configured, string expected)
    {
        var withoutReadOnly = expected.EndsWith(":ro", StringComparison.Ordinal)
            ? expected[..^3]
            : expected;

        return Values(configured).Any(value => value == expected || value == withoutReadOnly);
    }
}
