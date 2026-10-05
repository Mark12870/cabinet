using System.Security.Cryptography;
using System.Text;

namespace Cabinet.Core;

public sealed partial class Library
{
    public PrefixUpdate? PrefixUpdateOf(LibraryEntry entry) =>
        entry.Kind == PluginKind.Windows
        && Installed().TryGetValue(entry.Id, out var prefix)
        && prefix is not null
            ? PlanPrefixUpdate(entry, prefix).Review
            : null;

    public IReadOnlyDictionary<string, PrefixUpdate> PrefixUpdates()
    {
        var installed = Installed();
        var updates = new Dictionary<string, PrefixUpdate>(StringComparer.Ordinal);

        foreach (var entry in Entries())
        {
            if (entry.Kind == PluginKind.Windows
                && installed.TryGetValue(entry.Id, out var prefix)
                && prefix is not null
                && PlanPrefixUpdate(entry, prefix).Review is { Available: true } update)
            {
                updates.Add(entry.Id, update);
            }
        }

        return updates;
    }

    public IReadOnlyList<PrefixUpdate> PendingPrefixUpdates() => PerPrefix(PrefixUpdates().Values);

    public static IReadOnlyList<PrefixUpdate> PerPrefix(IEnumerable<PrefixUpdate> updates) =>
        [.. updates
            .GroupBy(update => update.Prefix, StringComparer.Ordinal)
            .Select(same => same.First())
            .OrderBy(update => update.Prefix, StringComparer.Ordinal)];

    public void UpdatePrefix(
        PrefixUpdate reviewed,
        Action<string>? onOutput = null,
        Action<double>? onProgress = null)
    {
        var prefixes = new Prefixes(layout, runner);
        using var claim = prefixes.Claim(reviewed.Prefix, $"update {reviewed.Entry.Name}'s prefix setup");
        var entry = Find(reviewed.Entry.Id);
        var prefix = Where(entry);
        var plan = PlanPrefixUpdate(entry, prefix);

        if (prefix != reviewed.Prefix || plan.Review.Stamp != reviewed.Stamp)
        {
            throw new InvalidOperationException(
                $"{entry.Name}'s catalogue setup or prefix changed since you reviewed it — "
                + "review the prefix update again");
        }

        if (!plan.Review.Available)
        {
            onOutput?.Invoke($"{entry.Name}'s prefix setup is already up to date.");
            return;
        }

        using var updating = Underway.Begin(layout.InstallLockPath(entry.Id))
                             ?? throw new InvalidOperationException(
                                 $"Cabinet is installing {entry.Name} right now — wait for it to finish");
        var log = layout.InstallLogPath(entry.Id);
        Directory.CreateDirectory(Path.GetDirectoryName(log)!);
        File.WriteAllText(log, "");

        void Say(string line)
        {
            LogFile.Append(log, line);
            onOutput?.Invoke(line);
        }

        Say($"Updating {entry.Name}'s prefix setup in {prefix}.");

        foreach (var kept in plan.Review.Preserved)
        {
            Say(kept);
        }

        var hasWork = plan.Runner is not null || plan.Sync is not null
                      || plan.Dxvk is not null || plan.Desktop is not null
                      || plan.Env.Count > 0 || plan.Winetricks.Count > 0;

        if (hasWork)
        {
            Settle(prefixes, prefix);

            try
            {
                ApplyPrefixSetup(plan, prefix, prefixes, Say, onProgress);
            }
            finally
            {
                Settle(prefixes, prefix);
            }

            Bridge(prefixes, Say);
        }

        if (plan.Review.Sharing.Count == 0)
        {
            plan.Desired.Save(layout.PrefixSetupFile(prefix));
        }
        Say($"{entry.Name}'s prefix setup is up to date.");
    }

    private void ApplyPrefixSetup(
        SetupUpdate plan,
        string prefix,
        Prefixes prefixes,
        Action<string> output,
        Action<double>? onProgress)
    {
        var settings = new PrefixSettings(layout);
        foreach (var (key, value) in plan.Env)
        {
            settings.SetVariable(prefix, key, value);
            output(value is null ? $"Removed {key}." : $"Set {key}.");
        }

        if (plan.Runner is { } wanted)
        {
            var name = wanted == Layout.BundledRunner
                ? wanted
                : EnsureRunner(wanted, output, onProgress);
            prefixes.MoveToRunner(prefix, name, output);
        }

        if (plan.Sync is { } sync)
        {
            prefixes.SetSync(prefix, sync, output);
        }

        if (plan.Winetricks.Count > 0)
        {
            var result = new Winetricks(layout, runner).Apply(prefix, plan.Winetricks, output);

            if (!result.Ok)
            {
                throw new InvalidOperationException(
                    $"{plan.Review.Entry.Name}'s Winetricks dependencies exited with {result.ExitCode}");
            }
        }

        if (plan.Dxvk is { } dxvk)
        {
            var operation = new Dxvk(layout, runner);
            if (dxvk)
            {
                operation.Install(prefix, output, onProgress);
            }
            else
            {
                operation.Remove(prefix, output);
            }
        }

        if (plan.Desktop is { } desktop)
        {
            var operation = new VirtualDesktop(layout, runner);
            if (desktop)
            {
                operation.Set(prefix, output);
            }
            else
            {
                operation.Unset(prefix, output);
            }
        }
    }

    private SetupUpdate PlanPrefixUpdate(LibraryEntry entry, string prefix)
    {
        var software = InstalledVersion(entry, prefix);
        var desired = PrefixSetup.From(PrefixConfig.For(entry.Configs, software), software);
        var settings = new PrefixSettings(layout);
        var dxvkVersion = new Dxvk(layout, runner).InstalledIn(prefix);
        var actual = new PrefixSetup(
            "",
            0,
            software,
            new Prefixes(layout, runner).RunnerOf(prefix),
            dxvkVersion is not null,
            settings.Sync(prefix),
            new Winetricks(layout, runner).Installed(prefix),
            settings.Variables(prefix),
            new VirtualDesktop(layout, runner).SavedEnabledIn(prefix));
        var family = FamilyOf(entry);
        var members = Recorded(prefix).Where(family.Contains).Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal).ToList();
        var foreign = Foreign(prefix, entry).Order(StringComparer.Ordinal).ToList();
        var free = foreign.Count == 0;
        var previous = free ? PrefixSetup.Read(layout.PrefixSetupFile(prefix)) : null;
        var changes = new List<string>();
        var kept = new List<string>();
        string? runnerChange = null;
        SyncMode? syncChange = null;
        bool? dxvkChange = null;
        bool? desktopChange = null;
        var env = new Dictionary<string, string?>(StringComparer.Ordinal);

        if (previous is null
                ? free && !RunnerMatches(actual.Runner!, desired.Runner)
                : previous.Runner != desired.Runner)
        {
            if (!RunnerMatches(actual.Runner!, desired.Runner))
            {
                if (previous is not null && RunnerMatches(actual.Runner!, previous.Runner) && free)
                {
                    runnerChange = desired.Runner ?? Layout.BundledRunner;
                    changes.Add($"Switch to Wine {RunnerWord(desired.Runner)}.");
                }
                else
                {
                    kept.Add($"Keeps Wine {actual.Runner} instead of the recommended {RunnerWord(desired.Runner)}.");
                }
            }
        }

        if (previous is null ? free && actual.Sync != desired.Sync : previous.Sync != desired.Sync)
        {
            if (actual.Sync != desired.Sync)
            {
                if (previous is not null && actual.Sync == previous.Sync && free)
                {
                    syncChange = desired.Sync;
                    changes.Add($"Switch the sync mode to {PrefixSettings.Word(desired.Sync)}.");
                }
                else
                {
                    kept.Add($"Keeps the {PrefixSettings.Word(actual.Sync)} sync mode instead of the recommended {PrefixSettings.Word(desired.Sync)}.");
                }
            }
        }

        if (previous is null ? free && desired.Dxvk && !actual.Dxvk : previous.Dxvk != desired.Dxvk)
        {
            if (actual.Dxvk != desired.Dxvk)
            {
                if ((previous is null || actual.Dxvk == previous.Dxvk) && free)
                {
                    dxvkChange = desired.Dxvk;
                    changes.Add(desired.Dxvk ? "Turn on DXVK for Direct3D." : "Turn off DXVK and use Wine's own Direct3D.");
                }
                else
                {
                    kept.Add(desired.Dxvk
                        ? "Keeps DXVK off instead of the recommended on."
                        : "Keeps DXVK on instead of the recommended off.");
                }
            }
        }

        if (previous is null ? free && desired.Desktop && !actual.Desktop : previous.Desktop != desired.Desktop)
        {
            if (actual.Desktop != desired.Desktop)
            {
                if ((previous is null || actual.Desktop == previous.Desktop) && free)
                {
                    desktopChange = desired.Desktop;
                    changes.Add(desired.Desktop ? "Turn on the Wine virtual desktop." : "Turn off the Wine virtual desktop.");
                }
                else
                {
                    kept.Add(desired.Desktop
                        ? "Keeps the virtual desktop off instead of the recommended on."
                        : "Keeps the virtual desktop on instead of the recommended off.");
                }
            }
        }

        foreach (var key in desired.Env.Keys.Concat(previous?.Env.Keys ?? [])
                     .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            var wanted = desired.Env.GetValueOrDefault(key);
            var before = previous?.Env.GetValueOrDefault(key);
            var current = actual.Env.GetValueOrDefault(key);

            if (!free || (previous is null ? current == wanted : wanted == before))
            {
                continue;
            }

            if (current != wanted)
            {
                if (current is null && before is null || previous is not null && current == before)
                {
                    env[key] = wanted;
                    changes.Add(wanted is null ? $"Remove the variable {key}." : $"Set {key} to {wanted}.");
                }
                else
                {
                    kept.Add($"Keeps {key} as it is instead of the recommended value.");
                }
            }
        }

        var verbs = desired.Winetricks
            .Except((previous ?? actual).Winetricks, StringComparer.Ordinal)
            .Except(actual.Winetricks, StringComparer.Ordinal)
            .ToList();
        changes.AddRange(verbs.Select(verb => $"Install {verb} with Winetricks."));
        var present = previous is null
            ? []
            : desired.Winetricks.Except(previous.Winetricks, StringComparer.Ordinal)
                .Intersect(actual.Winetricks, StringComparer.Ordinal).ToList();
        var dropped = previous is null
            ? []
            : previous.Winetricks.Except(desired.Winetricks, StringComparer.Ordinal).ToList();
        var revised = previous is null
            ? null
            : previous.ConfigVersion != desired.ConfigVersion
                ? $"{entry.Name} {software ?? desired.ConfigVersion} now uses Cabinet's setup for version "
                  + $"{desired.ConfigVersion}.\nPreviously version {previous.ConfigVersion}"
                : previous.Revision != desired.Revision
                    ? $"Cabinet's setup for {entry.Name} {desired.ConfigVersion} has changed.\n"
                      + $"Revision {previous.Revision} → {desired.Revision}"
                    : (previous with { Software = null }).Serialise() != (desired with { Software = null }).Serialise()
                        ? $"Cabinet's setup for {entry.Name} {desired.ConfigVersion} has changed."
                        : null;

        var state = string.Join('\n', entry.Id, prefix, previous?.Serialise() ?? "",
            desired.Serialise(), actual.Serialise(), dxvkVersion ?? "",
            string.Join('\n', members), string.Join('\n', foreign));
        var stamp = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(state)));
        var review = new PrefixUpdate(entry, prefix, previous is not null, desired.ConfigVersion, desired.Revision, previous?.Label,
            software, revised, changes, present, dropped, kept, members, foreign, verbs, stamp);
        return new SetupUpdate(review, desired, runnerChange, syncChange, dxvkChange, desktopChange, env, verbs);
    }

    private void RecordSetup(LibraryEntry entry, string prefix, bool created)
    {
        var receipt = layout.PrefixSetupFile(prefix);

        if (Foreign(prefix, entry).Count == 0 && (created || PrefixSetup.Read(receipt) is null))
        {
            PrefixSetup.From(entry.Config, InstalledVersion(entry, prefix)).Save(receipt);
        }
    }

    private string? InstalledVersion(LibraryEntry entry, string prefix)
    {
        if (entry.Family)
        {
            return null;
        }

        var keys = RecordedKeys(prefix, entry.Id).ToHashSet(StringComparer.Ordinal);

        return new PrefixRegistry(layout).Uninstallers(prefix)
                   .Where(one => keys.Contains(one.Key) && one.Version is not null && Names(one.Name, entry.Name))
                   .Select(one => one.Version)
                   .FirstOrDefault()
               ?? entry.Version;
    }

    private static bool RunnerMatches(string actual, string? wanted) =>
        wanted is null ? actual == Layout.BundledRunner : Answers(actual, wanted);

    private static string RunnerWord(string? wanted) => wanted ?? Layout.BundledRunner;

    private sealed record SetupUpdate(
        PrefixUpdate Review,
        PrefixSetup Desired,
        string? Runner,
        SyncMode? Sync,
        bool? Dxvk,
        bool? Desktop,
        IReadOnlyDictionary<string, string?> Env,
        IReadOnlyList<string> Winetricks);
}
