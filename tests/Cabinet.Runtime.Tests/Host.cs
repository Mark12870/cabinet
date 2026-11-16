using System.Diagnostics;
using System.Globalization;

namespace Cabinet.Runtime.Tests;

internal static class Host
{
    public const string App = "io.github.mark12870.cabinet";

    private static readonly TimeSpan TeardownPatience = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan TeardownTick = TimeSpan.FromMilliseconds(100);

    public static ProcessResult Run(string file, IReadOnlyList<string> arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(file)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            },
        };

        RuntimeTestEnvironment.Apply(process.StartInfo);

        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.Start();
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new ProcessResult(process.ExitCode, output, error);
    }

    public static void Configure(ProcessStartInfo info) => RuntimeTestEnvironment.Apply(info);

    public static string Location()
    {
        var result = Run("flatpak", ["info", "--user", "--show-location", App]);

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException("Cabinet is not installed for the current user: " + result.Error);
        }

        return result.Output.Trim();
    }

    public static string Shim() =>
        Path.Combine(Location(), "files", "lib", "yabridge", "cabinet-wine");

    public static IReadOnlyList<(string Instance, int Sandbox)> Instances(string app, string marker)
    {
        var result = Run("flatpak", ["ps", "--columns=instance,pid,child-pid,application"]);
        List<(string, int)> found = [];

        foreach (var line in result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = line.Split('\t', StringSplitOptions.TrimEntries);

            if (fields.Length < 4 || fields[3] != app)
            {
                continue;
            }

            if (CommandLine(fields[2]).Contains(marker, StringComparison.Ordinal))
            {
                found.Add((fields[0], int.Parse(fields[1], CultureInfo.InvariantCulture)));
            }
        }

        return found;
    }

    public static void Kill(string instance) => Run("flatpak", ["kill", instance]);

    public static void KillAll(string app, string marker)
    {
        var instances = Instances(app, marker);

        foreach (var (instance, _) in instances)
        {
            Kill(instance);
        }

        foreach (var (_, sandbox) in instances)
        {
            AwaitExit(sandbox);
        }
    }

    private static void AwaitExit(int pid)
    {
        var deadline = DateTime.UtcNow + TeardownPatience;

        while (Running(pid) && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(TeardownTick);
        }
    }

    private static bool Running(int pid)
    {
        try
        {
            var stat = File.ReadAllText($"/proc/{pid}/stat");
            return stat[stat.LastIndexOf(')') + 2] != 'Z';
        }
        catch (IOException)
        {
            return false;
        }
    }

    public static void Discard(string session)
    {
        try
        {
            Directory.Delete(session, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }

    private static string CommandLine(string pid)
    {
        try
        {
            return File.ReadAllText($"/proc/{pid}/cmdline").Replace('\0', ' ');
        }
        catch (SystemException)
        {
            return string.Empty;
        }
    }
}
