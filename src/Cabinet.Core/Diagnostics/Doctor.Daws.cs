namespace Cabinet.Core;

public sealed partial class Doctor
{
    public IReadOnlyList<string> DawsMissingPermissions() =>
        EnrolledDawIds().Where(dawId => EnrolledDaw(dawId).Status == Status.Fail).ToList();

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
            .Where(dawId => Enrolment.Override(dawId, layout) is { } ini && Enrolled(ini));
    }

    private static bool Enrolled(IniFile ini) =>
        ini.Get("Session Bus Policy", Layout.BridgeBusName) == "talk"
        || ini.Get("Environment", "WINELOADER") is { } loader
        && loader.Contains(Layout.AppId, StringComparison.Ordinal);

    private IEnumerable<Check> NativeDaw()
    {
        var entries = Layout.BridgedScanDirectories
            .Select(directory => (Scan: layout.WindowsScanDir(directory),
                Output: layout.BridgeOutputDir(directory)))
            .ToList();
        var owned = Enrolment.ScanLinkConflicts(layout);
        var unlinked = entries
            .Where(entry => !Directory.Exists(entry.Scan)
                            || new DirectoryInfo(entry.Scan).LinkTarget is not null
                            || new DirectoryInfo(entry.Output).LinkTarget != entry.Scan)
            .Select(entry => entry.Scan)
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

        if (Enrolment.UnmovedNative(layout) is { Count: > 0 } unmoved)
        {
            yield return new Check("native plugins", Status.Warn,
                $"still inside Cabinet's data, where a newly enrolled DAW cannot load them: "
                + $"{string.Join(", ", unmoved.Select(Path.GetFileName))} — make room in the folders they "
                + "belong in, or fix their permissions, and start Cabinet again");
        }

        var hasPlugins = entries.Any(entry => Directory.Exists(entry.Scan)
            && Directory.EnumerateFiles(entry.Scan, "*", SearchOption.AllDirectories)
                .Any(file => Path.GetExtension(file) is ".so" or ".clap"));

        yield return hasPlugins
            ? new Check("native DAWs", Status.Ok,
                $"Cabinet plugins are under {string.Join(", ", entries.Select(e => e.Scan))}; "
                + $"independent yabridge remains at {layout.NativeYabridgeDir}")
            : new Check("native DAWs", Status.Fail,
                "no Cabinet native plugins have been published — bridge what is installed again");
    }

    private Check EnrolledDaw(string dawId)
    {
        if (Enrolment.Override(dawId, layout) is not { } ini)
        {
            return new Check($"DAW {dawId}", Status.Fail, $"cannot read {layout.FlatpakOverride(dawId)}");
        }

        var missing = new List<string>();
        var hostCommands = ini.Get("Session Bus Policy", Layout.HostCommandBusName) == "talk";

        foreach (var argument in Enrolment.OverrideArguments(dawId, layout))
        {
            if (argument.StartsWith("--device=", StringComparison.Ordinal))
            {
                var device = argument["--device=".Length..];
                if (!Enrolment.Entries(ini.Get("Context", "devices")).Contains(device, StringComparer.Ordinal))
                {
                    missing.Add(argument);
                }
            }
            else if (argument.StartsWith("--filesystem=", StringComparison.Ordinal))
            {
                var filesystem = argument["--filesystem=".Length..];
                if (!Enrolment.Covers(layout, ini.Get("Context", "filesystems"), filesystem))
                {
                    missing.Add(argument);
                }
            }
            else if (argument.StartsWith("--talk-name=", StringComparison.Ordinal))
            {
                var name = argument["--talk-name=".Length..];
                if (ini.Get("Session Bus Policy", name) != "talk"
                    && !(name == Layout.BridgeBusName && hostCommands))
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

        if (missing.Count > 0)
        {
            return new Check($"DAW {dawId}", Status.Fail, "missing " + string.Join(", ", missing));
        }

        return hostCommands
            ? new Check($"DAW {dawId}", Status.Warn, Enrolment.HostCommandGrant(dawId))
            : Enrolment.Retirements(dawId, layout).Count > 0
                ? new Check($"DAW {dawId}", Status.Warn, Enrolment.OlderGrants(dawId))
                : new Check($"DAW {dawId}", Status.Ok, "enrolled — " + Enrolment.TrustBoundary(dawId));
    }
}
