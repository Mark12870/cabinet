using System.Diagnostics;
using Cabinet.Core;

namespace Cabinet.Cli;

internal static partial class Program
{
    private const string Usage = """
        Cabinet — Windows VST plugins on Linux, out of the box

        Usage:
          cabinet                              open the window
          cabinet enrol <daw-flatpak-id>       prepare a Flatpak DAW (prints the override)
          cabinet new <name> [runner]          create a Wine prefix, optionally on a runner
          cabinet install <name> <installer>   run a Windows installer in that prefix
          cabinet delete <name>                delete a prefix and everything in it
          cabinet list                         list prefixes
          cabinet use <name> <runner>          move a prefix to a runner and update it
          cabinet dxvk <name>                  install DXVK, the Direct3D some editors want
          cabinet show <name>                  everything a prefix is set to
          cabinet set <name> sync <mode>       system, esync, fsync or ntsync
          cabinet set <name> dxvk <on|off>     install DXVK, or put back what it replaced
          cabinet set <name> env KEY=VALUE     a variable for this prefix (KEY= removes it)
          cabinet set <name> desktop <on|off>  enable or disable a Wine desktop of its own
          cabinet winetricks <name> [verb...]  open Winetricks, or install the verbs given
          cabinet library                      plugins Cabinet knows how to install
          cabinet library show <id>            what a plugin is, and what installing costs
          cabinet library install <id> [--prefix <name>] [file]
                                               install one; demo entries download without a file
          cabinet library remove <id>          uninstall one; asks before taking its prefix
          cabinet library launch <id>          open a manager, bridging what it installs
          cabinet library stop <id>            close a manager Cabinet opened
          cabinet library open <link>          hand a sign-in link to the manager that registered it
          cabinet library log <id>             Cabinet and shared yabridge logs for an installed plugin
          cabinet runners                      list installed Wine runners
          cabinet runners available            list Wine versions you can install
          cabinet runners install <version>    download and unpack one
          cabinet runners add <archive>        unpack a Wine build you already have
          cabinet runners rm <runner>          delete a runner no prefix uses
          cabinet sync                         bridge again what changed outside Cabinet
          cabinet run <name> <cmd> [args...]   run a command in a prefix (winecfg, regedit)
          cabinet doctor                       check the setup end to end
          cabinet about                        which Cabinet this is, and what it bundles

        Options:
          --json                               JSON on stdout, from list, library, library show,
                                               doctor and about only
          --                                   no options after this; whatever follows the prefix
                                               of run and winetricks is passed on as it is

        Narrowing `cabinet library`:
          --search <text>                      every word must appear somewhere in the entry
          --category <name>                    Synth, Effect, Manager, Sampler, Drums, Bundle
          --developer <name>                   who makes it
          --kind <windows|linux>               under Wine, or native
          --installed, --not-installed         only what is here, or only what is not

        Exit status:
          0 done, 1 failed, 2 not a valid command, 3 declined at a prompt,
          4 no such prefix, plugin, runner, file or log. `run` exits with its command's status.
          Prompts and errors go to stderr.
        """;

    private static int Main(string[] args) =>
        Invoke(args, Layout.FromEnvironment, new ProcessRunner());

    internal static int Invoke(string[] args, Func<Layout> environment, IProcessRunner runner)
    {
        try
        {
            return args.Length == 0 ? LaunchGui() : Dispatch(CommandLine.Parse(args), environment, runner);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"cabinet: {exception.Message}");

            return exception switch
            {
                UsageException => Exit.Usage,
                KeyNotFoundException or FileNotFoundException or DirectoryNotFoundException => Exit.Absent,
                _ => Exit.Failed,
            };
        }
    }

    private static int LaunchGui()
    {
        using var gui = Process.Start(Layout.Gui)
            ?? throw new InvalidOperationException($"could not start {Layout.Gui}");

        gui.WaitForExit();
        return gui.ExitCode;
    }

    private static int Dispatch(CommandLine line, Func<Layout> environment, IProcessRunner runner)
    {
        if (line.Help)
        {
            Console.WriteLine(Usage);
            return Exit.Ok;
        }

        var layout = environment();
        var command = Parse(line, layout, runner);

        Bootstrap.Ensure(layout);
        return command();
    }

    private static Func<int> Parse(CommandLine line, Layout layout, IProcessRunner runner) =>
        line.Verb() switch
        {
            "enrol" or "enroll" => Enrol(line, layout),
            "new" => New(line, layout, runner),
            "library" => Library(line, layout, runner),
            "runners" => Runners(line, layout, runner),
            "use" => Use(line, layout, runner),
            "dxvk" => One(line, "a prefix name", name => InstallDxvk(layout, runner, name)),
            "show" => One(line, "a prefix name", name => Show(layout, runner, name)),
            "set" => Set(line, layout, runner),
            "winetricks" => RunWinetricks(line, layout, runner),
            "install" => Install(line, layout, runner),
            "delete" => One(line, "a prefix name", name => Delete(layout, runner, name)),
            "list" => line.Then(json => List(layout, runner, json)),
            "sync" => line.Then(() => Sync(layout, runner)),
            "run" => Run(line, layout, runner),
            "doctor" => line.Then(json => RunDoctor(layout, runner, json)),
            "about" => line.Then(json => ShowAbout(layout, runner, json)),
            var unknown => throw line.Unknown(unknown),
        };

    private static Func<int> One(CommandLine line, string what, Func<string, int> act)
    {
        var word = line.Word(what);
        return line.Then(() => act(word));
    }

    private static string Wrapped(string paragraph)
    {
        var lines = new List<string>();
        var line = "";

        foreach (var word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length + word.Length + 1 > 76)
            {
                lines.Add(line);
                line = "";
            }

            line = line.Length == 0 ? word : $"{line} {word}";
        }

        lines.Add(line);
        return string.Join(Environment.NewLine, lines);
    }

    private static bool Confirmed(string question)
    {
        Console.Error.Write(question);
        return string.Equals(Console.ReadLine()?.Trim(), "y", StringComparison.OrdinalIgnoreCase);
    }

    private static int LeftAlone()
    {
        Console.Error.WriteLine("Left alone.");
        return Exit.Declined;
    }

    private static int Exited(string what, ProcessResult result)
    {
        if (result.Ok)
        {
            return Exit.Ok;
        }

        Console.Error.WriteLine($"cabinet: {what} exited with {result.ExitCode}");
        return Exit.Failed;
    }
}
