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

    public IReadOnlyDictionary<string, PrefixUpdate> PrefixUpdates() =>
        PrefixReviews().Where(pair => pair.Value.Available)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

    public IReadOnlyDictionary<string, PrefixUpdate> PrefixReviews()
    {
        var installed = Installed();
        var reviews = new Dictionary<string, PrefixUpdate>(StringComparer.Ordinal);

        foreach (var entry in Entries())
        {
            if (entry.Kind == PluginKind.Windows
                && installed.TryGetValue(entry.Id, out var prefix)
                && prefix is not null)
            {
                reviews.Add(entry.Id, PlanPrefixUpdate(entry, prefix).Review);
            }
        }

        return reviews;
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
        Action<double>? onProgress = null,
        bool keepCustom = false) =>
        ChangePrefix(reviewed, restoring: false, keepCustom, onOutput, onProgress);

    public void RestorePrefix(
        PrefixUpdate reviewed,
        Action<string>? onOutput = null,
        Action<double>? onProgress = null) =>
        ChangePrefix(reviewed, restoring: true, keepCustom: false, onOutput, onProgress);

    private void ChangePrefix(
        PrefixUpdate reviewed,
        bool restoring,
        bool keepCustom,
        Action<string>? onOutput,
        Action<double>? onProgress)
    {
        var prefixes = new Prefixes(layout, runner);
        using var claim = prefixes.Claim(
            reviewed.Prefix, $"{(restoring ? "restore" : "update")} {reviewed.Entry.Name}'s prefix setup");
        var entry = Find(reviewed.Entry.Id);
        var prefix = Where(entry);
        var planned = PlanPrefixUpdate(entry, prefix);
        var plan = restoring ? planned.Restoring() : keepCustom ? planned.KeepingCustom() : planned;

        if (prefix != reviewed.Prefix || plan.Review.Stamp != reviewed.Stamp)
        {
            throw new InvalidOperationException(
                $"{entry.Name}'s catalogue setup or prefix changed since you reviewed it — "
                + $"review the prefix {(restoring ? "again" : "update again")}");
        }

        if (restoring ? plan.Review.Edits.Count == 0 : !plan.Review.Available)
        {
            onOutput?.Invoke(restoring
                ? $"{entry.Name}'s prefix already matches its config."
                : $"{entry.Name}'s prefix setup is already up to date.");
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

        Say($"{(restoring ? "Restoring" : "Updating")} {entry.Name}'s prefix setup in {prefix}.");

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
        Say(restoring
            ? $"{entry.Name}'s prefix matches its config again."
            : $"{entry.Name}'s prefix setup is up to date.");
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
        var previous = free ? Receipt(entry, prefix, software) : null;
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
        var changes = new List<string>();
        var resets = new List<string>();
        var custom = new HashSet<string>(StringComparer.Ordinal);
        string? runnerChange = null;
        SyncMode? syncChange = null;
        bool? dxvkChange = null;
        bool? desktopChange = null;
        var env = new Dictionary<string, string?>(StringComparer.Ordinal);

        void Offer(string setting, bool own, string change)
        {
            (own ? resets : changes).Add(change);

            if (own)
            {
                custom.Add(setting);
            }
        }

        if (previous is not null && revised is not null)
        {
            if (!RunnerMatches(actual.Runner!, desired.Runner))
            {
                runnerChange = desired.Runner ?? Layout.BundledRunner;
                Offer(RunnerSetting, !RunnerMatches(actual.Runner!, previous.Runner),
                    $"Switch Wine from {actual.Runner} to {RunnerWord(desired.Runner)}.");
            }

            if (actual.Sync != desired.Sync)
            {
                syncChange = desired.Sync;
                Offer(SyncSetting, actual.Sync != previous.Sync,
                    $"Switch the sync mode from {PrefixSettings.Word(actual.Sync)} to {PrefixSettings.Word(desired.Sync)}.");
            }

            if (actual.Dxvk != desired.Dxvk)
            {
                dxvkChange = desired.Dxvk;
                Offer(DxvkSetting, actual.Dxvk != previous.Dxvk,
                    desired.Dxvk ? "Turn on DXVK for Direct3D." : "Turn off DXVK and use Wine's own Direct3D.");
            }

            if (actual.Desktop != desired.Desktop)
            {
                desktopChange = desired.Desktop;
                Offer(DesktopSetting, actual.Desktop != previous.Desktop,
                    desired.Desktop ? "Turn on the Wine virtual desktop." : "Turn off the Wine virtual desktop.");
            }

            foreach (var key in desired.Env.Keys.Concat(previous.Env.Keys)
                         .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
            {
                var wanted = desired.Env.GetValueOrDefault(key);
                var current = actual.Env.GetValueOrDefault(key);

                if (current != wanted)
                {
                    env[key] = wanted;
                    Offer(EnvSetting + key, current != previous.Env.GetValueOrDefault(key),
                        wanted is null ? $"Remove the variable {key} (now {current})."
                        : current is null ? $"Set {key} to {wanted}."
                        : $"Change {key} from {current} to {wanted}.");
                }
            }
        }

        var edits = previous is null ? [] : Edits(previous, actual);
        var verbs = desired.Winetricks.Except(actual.Winetricks, StringComparer.Ordinal).ToList();
        changes.AddRange(verbs.Select(verb => $"Install {verb} with Winetricks."));
        var present = previous is null
            ? []
            : desired.Winetricks.Except(previous.Winetricks, StringComparer.Ordinal)
                .Intersect(actual.Winetricks, StringComparer.Ordinal).ToList();
        var dropped = previous is null
            ? []
            : previous.Winetricks.Except(desired.Winetricks, StringComparer.Ordinal).ToList();

        var state = string.Join('\n', entry.Id, prefix, previous?.Serialise() ?? "",
            desired.Serialise(), actual.Serialise(), dxvkVersion ?? "",
            string.Join('\n', members), string.Join('\n', foreign));
        var stamp = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(state)));
        var review = new PrefixUpdate(entry, prefix, desired.ConfigVersion, desired.Revision, previous?.Label,
            software, revised, changes, resets, edits, present, dropped, members, foreign, verbs, stamp);
        return new SetupUpdate(
            review, desired, runnerChange, syncChange, dxvkChange, desktopChange, env, verbs, custom, previous, actual);
    }

    private void RecordSetup(LibraryEntry entry, string prefix, bool created)
    {
        if (Foreign(prefix, entry).Count > 0)
        {
            return;
        }

        if (created)
        {
            PrefixSetup.From(entry.Config, InstalledVersion(entry, prefix)).Save(layout.PrefixSetupFile(prefix));
        }
        else
        {
            Receipt(entry, prefix, InstalledVersion(entry, prefix));
        }
    }

    private PrefixSetup Receipt(LibraryEntry entry, string prefix, string? software)
    {
        var file = layout.PrefixSetupFile(prefix);

        if (PrefixSetup.Read(file) is { } recorded)
        {
            return recorded;
        }

        var first = PrefixSetup.From(PrefixConfig.First(entry.Configs), software);

        if (Directory.Exists(layout.PrefixPath(prefix)))
        {
            try
            {
                first.Save(file);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
            }
        }

        return first;
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

    private static List<string> Edits(PrefixSetup applied, PrefixSetup actual)
    {
        var edits = new List<string>();

        if (!RunnerMatches(actual.Runner!, applied.Runner))
        {
            edits.Add($"Wine {actual.Runner} instead of {RunnerWord(applied.Runner)}.");
        }

        if (actual.Sync != applied.Sync)
        {
            edits.Add($"Sync mode {PrefixSettings.Word(actual.Sync)} instead of {PrefixSettings.Word(applied.Sync)}.");
        }

        if (actual.Dxvk != applied.Dxvk)
        {
            edits.Add(actual.Dxvk ? "DXVK on instead of off." : "DXVK off instead of on.");
        }

        if (actual.Desktop != applied.Desktop)
        {
            edits.Add(actual.Desktop ? "Virtual desktop on instead of off." : "Virtual desktop off instead of on.");
        }

        foreach (var (key, value) in applied.Env.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var current = actual.Env.GetValueOrDefault(key);

            if (current != value)
            {
                edits.Add(current is null ? $"{key} removed instead of {value}." : $"{key} {current} instead of {value}.");
            }
        }

        return edits;
    }

    private static bool RunnerMatches(string actual, string? wanted) =>
        wanted is null ? actual == Layout.BundledRunner : Answers(actual, wanted);

    private static string RunnerWord(string? wanted) => wanted ?? Layout.BundledRunner;

    private const string RunnerSetting = "runner";

    private const string SyncSetting = "sync";

    private const string DxvkSetting = "dxvk";

    private const string DesktopSetting = "desktop";

    private const string EnvSetting = "env:";

    private sealed record SetupUpdate(
        PrefixUpdate Review,
        PrefixSetup Desired,
        string? Runner,
        SyncMode? Sync,
        bool? Dxvk,
        bool? Desktop,
        IReadOnlyDictionary<string, string?> Env,
        IReadOnlyList<string> Winetricks,
        IReadOnlySet<string> Custom,
        PrefixSetup? Applied,
        PrefixSetup Actual)
    {
        public SetupUpdate Restoring() => Applied is not { } applied
            ? this
            : this with
            {
                Desired = applied,
                Runner = RunnerMatches(Actual.Runner!, applied.Runner) ? null : applied.Runner ?? Layout.BundledRunner,
                Sync = Actual.Sync == applied.Sync ? null : applied.Sync,
                Dxvk = Actual.Dxvk == applied.Dxvk ? null : applied.Dxvk,
                Desktop = Actual.Desktop == applied.Desktop ? null : applied.Desktop,
                Env = applied.Env.Where(pair => Actual.Env.GetValueOrDefault(pair.Key) != pair.Value)
                    .ToDictionary(pair => pair.Key, string? (pair) => pair.Value, StringComparer.Ordinal),
                Winetricks = [],
            };

        public SetupUpdate KeepingCustom() => this with
        {
            Runner = Custom.Contains(RunnerSetting) ? null : Runner,
            Sync = Custom.Contains(SyncSetting) ? null : Sync,
            Dxvk = Custom.Contains(DxvkSetting) ? null : Dxvk,
            Desktop = Custom.Contains(DesktopSetting) ? null : Desktop,
            Env = Env.Where(pair => !Custom.Contains(EnvSetting + pair.Key))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
        };
    }
}
