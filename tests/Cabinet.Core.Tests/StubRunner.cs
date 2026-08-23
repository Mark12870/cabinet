using Cabinet.Core;

namespace Cabinet.Core.Tests;

internal sealed class StubRunner(ProcessResult result) : IProcessRunner
{
    public ProcessResult Run(
        string file,
        IReadOnlyList<string> args,
        IReadOnlyDictionary<string, string>? env = null,
        Action<string>? onOutput = null,
        string? workingDirectory = null,
        bool capture = true) =>
        result;
}
