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
                $"no initialised prefix '{prefix}' — create it first");
        }

        var prefixes = new Prefixes(layout, runner);
        using var claim = prefixes.Claim(prefix, $"let Winetricks change {prefix}");

        return runner.Run(
            Layout.Winetricks,
            ["--unattended", .. verbs],
            prefixes.Variables(prefix),
            onOutput,
            layout.PrefixPath(prefix));
    }
}
