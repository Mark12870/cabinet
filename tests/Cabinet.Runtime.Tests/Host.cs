using System.Diagnostics;

namespace Cabinet.Runtime.Tests;

internal static class Host
{
    public const string App = "io.github.mark12870.cabinet";

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

    public static bool Installed(string app) => Run("flatpak", ["info", app]).ExitCode == 0;

    public static IReadOnlyList<string> Instances(string app, string marker)
    {
        var result = Run("flatpak", ["ps", "--columns=instance,child-pid,application"]);
        List<string> found = [];

        foreach (var line in result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = line.Split('\t', StringSplitOptions.TrimEntries);

            if (fields.Length < 3 || fields[2] != app)
            {
                continue;
            }

            if (CommandLine(fields[1]).Contains(marker, StringComparison.Ordinal))
            {
                found.Add(fields[0]);
            }
        }

        return found;
    }

    public static void Kill(string instance) => Run("flatpak", ["kill", instance]);

    public static void KillAll(string app, string marker)
    {
        foreach (var instance in Instances(app, marker))
        {
            Kill(instance);
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
