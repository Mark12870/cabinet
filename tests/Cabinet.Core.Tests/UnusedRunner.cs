using Cabinet.Core;

namespace Cabinet.Core.Tests;

internal sealed class UnusedRunner : IProcessRunner
{
    public ProcessResult Run(
        string file,
        IReadOnlyList<string> args,
        IReadOnlyDictionary<string, string>? env = null,
        Action<string>? onOutput = null,
        string? workingDirectory = null) =>
        throw new NotSupportedException($"this operation should run no process, got '{file}'");
}
