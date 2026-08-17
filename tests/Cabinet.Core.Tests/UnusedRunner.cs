using Cabinet.Core;

namespace Cabinet.Core.Tests;

/// <summary>
/// For operations that touch no external command. Throwing rather than returning a stub
/// result keeps a test honest: if the code under test grows a process call, the test fails
/// instead of quietly passing against a fake.
/// </summary>
internal sealed class UnusedRunner : IProcessRunner
{
    public ProcessResult Run(
        string file,
        IReadOnlyList<string> args,
        IReadOnlyDictionary<string, string>? env = null,
        bool inherit = false) =>
        throw new NotSupportedException($"this operation should run no process, got '{file}'");
}
