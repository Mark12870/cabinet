using Cabinet.Core;

namespace Cabinet.Cli;

internal static partial class Program
{
    private static Func<int> New(CommandLine line, Layout layout, IProcessRunner runner)
    {
        var name = line.Word("a prefix name");
        var runnerName = line.OptionalWord();

        return line.Then(() =>
        {
            var prefix = new Prefixes(layout, runner).Create(name, runnerName, Console.WriteLine);
            Console.WriteLine($"{prefix.Name}  {prefix.Path}  ({prefix.Runner})");
            Console.WriteLine($"Install plugins with `cabinet install {prefix.Name} <installer>`.");
            return Exit.Ok;
        });
    }

    private static Func<int> Use(CommandLine line, Layout layout, IProcessRunner runner)
    {
        var name = line.Word("a prefix name");
        var runnerName = line.Word("a runner name");

        return line.Then(() =>
        {
            new Prefixes(layout, runner).MoveToRunner(name, runnerName, Console.WriteLine);
            Console.WriteLine($"{name} now runs on {runnerName}.");
            return Exit.Ok;
        });
    }

    private static int InstallDxvk(Layout layout, IProcessRunner runner, string name)
    {
        new Dxvk(layout, runner).Install(name, Console.WriteLine);

        Console.WriteLine("Reopen the plugin in your DAW; its editor should redraw as you use it.");
        return Exit.Ok;
    }

    private static int RemoveDxvk(Layout layout, IProcessRunner runner, string name)
    {
        new Dxvk(layout, runner).Remove(name, Console.WriteLine);
        return Exit.Ok;
    }

    private static Func<int> Set(CommandLine line, Layout layout, IProcessRunner runner)
    {
        var name = line.Word("a prefix name");

        return line.Subcommand() switch
        {
            "sync" => One(line, "a sync mode", word => SetSync(layout, runner, name, Sync(word))),
            "dxvk" => One(line, "on or off", word => OnOff(word)
                ? InstallDxvk(layout, runner, name)
                : RemoveDxvk(layout, runner, name)),
            "env" => One(line, "KEY=VALUE", assignment => SetVariable(layout, name, Assignment(assignment))),
            "desktop" => One(line, "on or off", word => SetDesktop(layout, runner, name, OnOff(word))),
            null => throw new UsageException("expected sync, dxvk, env or desktop"),
            var unknown => throw line.Unknown($"set {name} {unknown}"),
        };
    }

    private static Func<int> RunWinetricks(CommandLine line, Layout layout, IProcessRunner runner)
    {
        var name = line.Word("a prefix name");
        var verbs = line.PassedThrough;

        return line.Then(() =>
        {
            Console.WriteLine(Wrapped(Winetricks.Consent(verbs)));
            Console.WriteLine();
            var winetricks = new Winetricks(layout, runner);
            var result = verbs.Count == 0
                ? winetricks.Open(name, Console.WriteLine)
                : winetricks.Apply(name, verbs, Console.WriteLine);
            new Prefixes(layout, runner).Bridge(Console.WriteLine);

            return Exited("winetricks", result);
        });
    }

    private static bool OnOff(string word) => word.Trim().ToLowerInvariant() switch
    {
        "on" => true,
        "off" => false,
        _ => throw new UsageException($"expected on or off, not '{word}'"),
    };

    private static SyncMode Sync(string word)
    {
        try
        {
            return PrefixSettings.ParseSync(word);
        }
        catch (ArgumentException invalid)
        {
            throw new UsageException(invalid.Message);
        }
    }

    private static int SetDesktop(Layout layout, IProcessRunner runner, string name, bool on)
    {
        var desktop = new VirtualDesktop(layout, runner);

        if (on)
        {
            desktop.Set(name, Console.WriteLine);
        }
        else
        {
            desktop.Unset(name, Console.WriteLine);
        }

        return Exit.Ok;
    }

    private static int SetSync(Layout layout, IProcessRunner runner, string name, SyncMode mode)
    {
        new Prefixes(layout, runner).SetSync(name, mode, Console.WriteLine);
        return Exit.Ok;
    }

    private static int SetVariable(Layout layout, string name, (string Key, string? Value) assignment)
    {
        var (key, value) = assignment;
        new PrefixSettings(layout).SetVariable(name, key, value);

        Console.WriteLine(value is null
            ? $"{key} removed from {name}."
            : $"{key}={value} in {name}.");
        return Exit.Ok;
    }

    private static (string Key, string? Value) Assignment(string text)
    {
        var at = text.IndexOf('=');

        if (at <= 0)
        {
            throw new UsageException($"expected <name>=<value>, got '{text}'");
        }

        var value = text[(at + 1)..];
        return (text[..at], value.Length == 0 ? null : value);
    }

    private static int Show(Layout layout, IProcessRunner runner, string name)
    {
        var prefix = new Prefixes(layout, runner).List()
            .FirstOrDefault(candidate => candidate.Name == name)
            ?? throw new KeyNotFoundException($"no such prefix '{name}'");

        Console.WriteLine($"{"name",-16}  {prefix.Name}");
        Console.WriteLine($"{"path",-16}  {prefix.Path}");
        Console.WriteLine($"{"state",-16}  {(prefix.Initialised ? "initialised" : "bare")}");
        Console.WriteLine($"{"runner",-16}  {prefix.Runner}");
        Console.WriteLine($"{"dxvk",-16}  {prefix.Dxvk ?? "off"}");
        Console.WriteLine($"{"sync",-16}  {PrefixSettings.Word(prefix.Sync)}");
        Console.WriteLine($"{"desktop",-16}  {(prefix.Desktop ? "on" : "off")}");

        Describe("env", new PrefixSettings(layout).Variables(name));

        return Exit.Ok;
    }

    private static void Describe(string label, IReadOnlyDictionary<string, string> entries)
    {
        if (entries.Count == 0)
        {
            Console.WriteLine($"{label,-16}  none");
            return;
        }

        foreach (var (key, value) in entries.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            Console.WriteLine($"{label,-16}  {key}={value}");
        }
    }

    private static Func<int> Install(CommandLine line, Layout layout, IProcessRunner runner)
    {
        var name = line.Word("a prefix name");
        var installer = line.Word("an installer path");

        return line.Then(() => Exited(
            Path.GetFileName(installer),
            new Prefixes(layout, runner).Install(name, installer, Console.WriteLine)));
    }

    private static int Delete(Layout layout, IProcessRunner runner, string name)
    {
        var prefixes = new Prefixes(layout, runner);
        var prefix = prefixes.List().FirstOrDefault(candidate => candidate.Name == name)
                     ?? throw new KeyNotFoundException($"no such prefix '{name}'");

        if (!Confirmed($"Delete '{prefix.Name}' and every plugin installed in it? [y/N] "))
        {
            return LeftAlone();
        }

        prefixes.Delete(prefix.Name, Console.WriteLine);
        return Exit.Ok;
    }

    private static int List(Layout layout, IProcessRunner runner, bool json)
    {
        var prefixes = new Prefixes(layout, runner).List();

        if (json)
        {
            Console.WriteLine(Json.Prefixes(prefixes));
            return Exit.Ok;
        }

        if (prefixes.Count == 0)
        {
            Console.WriteLine("No prefixes yet. Create one with `cabinet new <name>`.");
            return Exit.Ok;
        }

        foreach (var prefix in prefixes)
        {
            Console.WriteLine(
                $"{(prefix.Initialised ? "ok  " : "bare")}  {prefix.Name,-20}  "
                + $"{prefix.Runner,-24}  {(prefix.Dxvk is null ? "" : "dxvk " + prefix.Dxvk),-11}"
                + $"  {(prefix.Desktop ? "desktop" : ""),-9}"
                + $"  {prefix.Path}");
        }

        return Exit.Ok;
    }

    private static Func<int> Run(CommandLine line, Layout layout, IProcessRunner runner)
    {
        var name = line.Word("a prefix name");
        var command = line.PassedThrough.FirstOrDefault() ?? throw new UsageException("expected a command");
        var arguments = line.PassedThrough.Skip(1).ToArray();

        return line.Then(() =>
        {
            var prefixes = new Prefixes(layout, runner);
            var result = prefixes.Run(name, command, arguments, interactive: true);
            prefixes.Bridge(Console.Error.WriteLine);
            return result.ExitCode;
        });
    }
}
