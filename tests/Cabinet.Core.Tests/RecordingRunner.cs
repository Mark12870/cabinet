using Cabinet.Core;

namespace Cabinet.Core.Tests;

internal sealed class RecordingRunner(Action<IReadOnlyList<string>>? acts = null) : IProcessRunner
{
    private readonly List<Call> calls = [];

    internal sealed record Call(
        string File,
        IReadOnlyList<string> Arguments,
        IReadOnlyDictionary<string, string> Environment,
        string? WorkingDirectory,
        string? LogTo);

    public IReadOnlyList<Call> Calls => calls;

    public IReadOnlyDictionary<string, string> Environment { get; private set; } =
        new Dictionary<string, string>();

    public string LastFile { get; private set; } = "";

    public IReadOnlyList<string> LastArguments { get; private set; } = [];

    public ProcessResult Run(
        string file,
        IReadOnlyList<string> args,
        IReadOnlyDictionary<string, string>? env = null,
        Action<string>? onOutput = null,
        string? workingDirectory = null,
        string? logTo = null)
    {
        Environment = env ?? new Dictionary<string, string>();
        calls.Add(new Call(file, args, Environment, workingDirectory, logTo));
        LastFile = file;
        LastArguments = args;
        acts?.Invoke(args);
        return new ProcessResult(0, "", "");
    }
}
