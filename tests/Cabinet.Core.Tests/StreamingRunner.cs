using Cabinet.Core;

namespace Cabinet.Core.Tests;

internal sealed class StreamingRunner(params string[] lines) : IProcessRunner
{
    public IReadOnlyList<string> LastArguments { get; private set; } = [];

    public ProcessResult Run(
        string file,
        IReadOnlyList<string> args,
        IReadOnlyDictionary<string, string>? env = null,
        Action<string>? onOutput = null,
        string? workingDirectory = null,
        bool capture = true)
    {
        LastArguments = args;

        foreach (var line in lines)
        {
            onOutput?.Invoke(line);
        }

        return new ProcessResult(0, "", "");
    }
}
