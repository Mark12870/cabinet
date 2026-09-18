using Cabinet.Core;

namespace Cabinet.Core.Tests;

internal sealed class RecordingRunner(
    Action<IReadOnlyList<string>>? acts = null,
    Func<IReadOnlyList<string>, int>? exits = null,
    Func<IReadOnlyList<string>, string>? outputs = null,
    bool dawSession = false,
    Action? paths = null) : IProcessRunner
{
    private readonly List<Call> calls = [];
    private int joining;
    private bool joined;

    internal sealed record Call(
        string File,
        IReadOnlyList<string> Arguments,
        IReadOnlyDictionary<string, string> Environment,
        string? WorkingDirectory,
        string? LogTo,
        IReadOnlySet<string> BlankEnvironment,
        bool InheritStdin);

    public IReadOnlyList<Call> Calls => calls;

    public IReadOnlyList<Call> Ran =>
        calls
            .Where(call => call.Arguments is not [Prefixes.SessionMode])
            .Where(call => call.Arguments is not [Prefixes.PathsMode])
            .ToList();

    public IReadOnlyDictionary<string, string> Environment { get; private set; } =
        new Dictionary<string, string>();

    public string LastFile { get; private set; } = "";

    public IReadOnlyList<string> LastArguments { get; private set; } = [];

    public void Retire()
    {
        if (joining > 0)
        {
            throw new InvalidOperationException(
                "a session cannot retire while one of its jobs is still running");
        }

        joined = false;
    }

    public ProcessResult Run(
        string file,
        IReadOnlyList<string> args,
        IReadOnlyDictionary<string, string>? env = null,
        Action<string>? onOutput = null,
        string? workingDirectory = null,
        string? logTo = null,
        IReadOnlySet<string>? blankEnvironment = null,
        bool inheritStdin = false)
    {
        Environment = env ?? new Dictionary<string, string>();
        calls.Add(new Call(
            file, args, Environment, workingDirectory, logTo,
            blankEnvironment ?? new HashSet<string>(), inheritStdin));
        LastFile = file;
        LastArguments = args;

        if (args is [Prefixes.PathsMode])
        {
            paths?.Invoke();
            return new ProcessResult(0, SessionFiles.Printed(Environment), "");
        }

        if (args is [Prefixes.SessionMode])
        {
            return dawSession || joined
                ? new ProcessResult(0, Prefixes.SessionLiveWord + "\n", "")
                : new ProcessResult(1, "", "");
        }

        var joins = args is [Prefixes.JoinMode, ..];
        joining += joins ? 1 : 0;
        joined |= joins;

        try
        {
            acts?.Invoke(args);
            return new ProcessResult(exits?.Invoke(args) ?? 0, outputs?.Invoke(args) ?? "", "");
        }
        finally
        {
            joining -= joins ? 1 : 0;
        }
    }
}
