using Cabinet.Core;

namespace Cabinet.Core.Tests;

internal sealed class RecordingRunner : IProcessRunner
{
    public IReadOnlyDictionary<string, string> Environment { get; private set; } =
        new Dictionary<string, string>();

    public string LastFile { get; private set; } = "";

    public IReadOnlyList<string> LastArguments { get; private set; } = [];

    public ProcessResult Run(
        string file,
        IReadOnlyList<string> args,
        IReadOnlyDictionary<string, string>? env = null,
        Action<string>? onOutput = null)
    {
        Environment = env ?? new Dictionary<string, string>();
        LastFile = file;
        LastArguments = args;
        return new ProcessResult(0, "", "");
    }
}
