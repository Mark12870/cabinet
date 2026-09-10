namespace Cabinet.Core;

public sealed class Winetricks(Layout layout, IProcessRunner runner)
{
    public ProcessResult Apply(
        string prefix, IReadOnlyList<string> verbs, Action<string>? onOutput = null) =>
        Run(prefix, verbs, onOutput);

    public ProcessResult Open(string prefix, Action<string>? onOutput = null) =>
        Run(prefix, [], onOutput);

    private ProcessResult Run(
        string prefix, IReadOnlyList<string> verbs, Action<string>? onOutput)
    {
        if (!Directory.Exists(Path.Combine(layout.PrefixPath(prefix), "dosdevices")))
        {
            throw new DirectoryNotFoundException(
                $"no initialised prefix '{prefix}' — make one with `cabinet new {prefix}`");
        }

        var prefixes = new Prefixes(layout, runner);

        if (prefixes.SessionLive(prefix))
        {
            throw new InvalidOperationException(
                $"Winetricks cannot change '{prefix}' while its Wine session is active. "
                + "Close every DAW and Cabinet application using this prefix, wait a few seconds, and try again");
        }

        return runner.Run(
            Layout.Winetricks,
            ["--unattended", .. verbs],
            prefixes.Variables(prefix),
            onOutput,
            layout.PrefixPath(prefix));
    }
}
