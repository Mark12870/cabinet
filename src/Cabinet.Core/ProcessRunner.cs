using System.Diagnostics;

namespace Cabinet.Core;

public sealed record ProcessResult(int ExitCode, string Stdout, string Stderr)
{
    public bool Ok => ExitCode == 0;
}

/// <summary>Runs external commands. An interface so operations stay testable.</summary>
public interface IProcessRunner
{
    ProcessResult Run(string file, IReadOnlyList<string> args, IReadOnlyDictionary<string, string>? env = null);
}

public sealed class ProcessRunner : IProcessRunner
{
    public ProcessResult Run(
        string file,
        IReadOnlyList<string> args,
        IReadOnlyDictionary<string, string>? env = null)
    {
        var info = new ProcessStartInfo(file)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
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

        // Read before waiting: a process that fills a pipe buffer blocks forever otherwise.
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return new ProcessResult(process.ExitCode, stdout, stderr);
    }
}
