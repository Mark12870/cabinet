using Cabinet.Core;

namespace Cabinet.Cli;

internal static partial class Program
{
    private static Func<int> Runners(CommandLine line, Layout layout, IProcessRunner runner) =>
        line.Subcommand() switch
        {
            null => line.Then(() => ListRunners(layout, runner)),
            "available" => line.Then(() => AvailableRunners(runner)),
            "install" => One(line, "a Wine version", version => InstallRunner(layout, runner, version)),
            "add" => One(line, "an archive path", archive => AddRunner(layout, runner, archive)),
            "rm" => One(line, "a runner name", name => RemoveRunner(layout, runner, name)),
            var unknown => throw line.Unknown($"runners {unknown}"),
        };

    private static int ListRunners(Layout layout, IProcessRunner runner)
    {
        var runners = new Runners(layout, runner);

        foreach (var found in runners.List())
        {
            var by = string.Join(", ", runners.InUseBy(found.Name));

            Console.WriteLine(
                $"{(found.Usable ? "ok  " : "FAIL")}  {found.Name,-30}  "
                + $"{(found.Multilib ? "32+64" : "64   ")}  {runners.Version(found),-34}  {by}");
        }

        return Exit.Ok;
    }

    private static int AvailableRunners(IProcessRunner runner)
    {
        var families = new RunnerIndex(runner).Available().GroupBy(release => release.Family);

        foreach (var family in families)
        {
            Console.WriteLine($"{family.Key.Label} — {family.Key.Description}");

            foreach (var release in family)
            {
                var pinned = RunnerIndex.IsPinned(release) ? "  pinned" : "";
                Console.WriteLine($"  {release.Version,-10}  {release.Name}{pinned}");
            }

            Console.WriteLine();
        }

        Console.WriteLine(RunnerIndex.Provenance);
        return Exit.Ok;
    }

    private static int InstallRunner(Layout layout, IProcessRunner runner, string version)
    {
        var release = new RunnerIndex(runner).Find(version);
        var installed = new Runners(layout, runner).Install(release, Console.WriteLine);

        Console.WriteLine();
        Console.WriteLine($"{installed.Name}  {installed.Wine}");
        Console.WriteLine($"Put a prefix on it with `cabinet use <prefix> {installed.Name}`.");
        return Exit.Ok;
    }

    private static int AddRunner(Layout layout, IProcessRunner runner, string archive)
    {
        var added = new Runners(layout, runner).Add(archive, onOutput: Console.WriteLine);

        Console.WriteLine();
        Console.WriteLine($"{added.Name}  {added.Wine}");
        Console.WriteLine($"Put a prefix on it with `cabinet use <prefix> {added.Name}`.");
        return Exit.Ok;
    }

    private static int RemoveRunner(Layout layout, IProcessRunner runner, string name)
    {
        new Runners(layout, runner).Remove(name);
        Console.WriteLine($"Deleted {layout.RunnerPath(name)}");
        return Exit.Ok;
    }
}
