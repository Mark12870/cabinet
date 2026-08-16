using System.Diagnostics;

namespace Cabinet.Core;

public sealed record ProcessResult(int ExitCode, string Stdout, string Stderr)
{
    public bool Ok => ExitCode == 0;
}

/// <summary>Runs external commands. An interface so operations stay testable.</summary>
public interface IProcessRunner
{
    /// <param name="inherit">Write straight to this process's stdio; no capture.</param>
    ProcessResult Run(
        string file,
        IReadOnlyList<string> args,
        IReadOnlyDictionary<string, string>? env = null,
        bool inherit = false);
}

public sealed class ProcessRunner : IProcessRunner
{
    public ProcessResult Run(
        string file,
        IReadOnlyList<string> args,
        IReadOnlyDictionary<string, string>? env = null,
        bool inherit = false)
    {
        var info = new ProcessStartInfo(file)
        {
            RedirectStandardOutput = !inherit,
            RedirectStandardError = !inherit,
            UseShellExecute = false,
        };

        foreach (var arg in args)
        {
            info.ArgumentList.Add(arg);
        }

        if (env is not null)
        {
            foreach (var (key, value) in env)
            {
                info.Environment[key] = value;
            }
        }

        using var process = Process.Start(info)
                            ?? throw new InvalidOperationException($"could not start {file}");

        if (inherit)
        {
            process.WaitForExit();
            return new ProcessResult(process.ExitCode, "", "");
        }

        // Drained concurrently: reading one to the end first deadlocks once the child
        // fills the other's buffer, which Wine's fixme spam does during an install.
        var stderr = Task.Run(process.StandardError.ReadToEnd);
        var stdout = process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        return new ProcessResult(process.ExitCode, stdout, stderr.Result);
    }
}
