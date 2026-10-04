using Cabinet.Core;

namespace Cabinet.Cli;

internal static partial class Program
{
    private static int RunDoctor(Layout layout, IProcessRunner runner, bool json)
    {
        var checks = new Doctor(layout, runner).Run();

        if (json)
        {
            Console.WriteLine(Json.Checks(checks));
        }
        else
        {
            foreach (var check in checks)
            {
                var mark = check.Status switch
                {
                    Status.Ok => "ok  ",
                    Status.Warn => "warn",
                    _ => "FAIL",
                };

                Console.WriteLine($"{mark}  {check.Name,-24}  {check.Detail}");
            }
        }

        return checks.Any(check => check.Status == Status.Fail) ? Exit.Failed : Exit.Ok;
    }

    private static int ShowAbout(Layout layout, IProcessRunner runner, bool json)
    {
        var build = new About(layout, runner).Read();

        if (json)
        {
            Console.WriteLine(Json.Build(build));
            return Exit.Ok;
        }

        Console.WriteLine($"{"version",-16}  {build.Version}");
        Console.WriteLine($"{"installed from",-16}  {Describe(build)}");
        Console.WriteLine($"{"commit",-16}  {build.Commit}");
        Console.WriteLine($"{"yabridge",-16}  {build.Yabridge}");
        Console.WriteLine($"{"wine",-16}  {build.Wine}");
        Console.WriteLine($"{"prefixes",-16}  {layout.PrefixesDir}");
        Console.WriteLine($"{"runners",-16}  {layout.RunnersDir}");
        Console.WriteLine($"{"sockets",-16}  {layout.SocketDir}");
        Console.WriteLine($"{"runtime log",-16}  {layout.RuntimeLogPath}");
        Console.WriteLine($"{"yabridge dir",-16}  {layout.HostYabridgeDir}");

        if (build.Homepage is { } homepage)
        {
            Console.WriteLine($"{"homepage",-16}  {homepage}");
        }

        if (build.BugTracker is { } tracker)
        {
            Console.WriteLine($"{"issues",-16}  {tracker}");
        }

        return Exit.Ok;
    }

    private static int ShowChangelog(bool json)
    {
        var releases = About.Changelog();

        if (json)
        {
            Console.WriteLine(Json.Changelog(releases));
            return Exit.Ok;
        }

        foreach (var release in releases)
        {
            Console.WriteLine($"{release.Version}  {release.Date}  {release.Headline}");
            foreach (var change in release.Changes)
            {
                Console.WriteLine($"  - {change}");
            }

            Console.WriteLine();
        }

        return Exit.Ok;
    }

    private static string Describe(Build build) => build.Origin switch
    {
        Origin.Published => $"{build.Remote}  ({build.Url}) — published build",
        Origin.Local => $"{build.Remote}  ({build.Url}) — local build",
        _ => $"{build.Remote} — cannot tell whether it is published",
    };
}
