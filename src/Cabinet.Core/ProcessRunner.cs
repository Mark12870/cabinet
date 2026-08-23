using System.Diagnostics;

namespace Cabinet.Core;

public sealed record ProcessResult(int ExitCode, string Stdout, string Stderr)
{
    public bool Ok => ExitCode == 0;
}

public interface IProcessRunner
{
    ProcessResult Run(
        string file,
        IReadOnlyList<string> args,
        IReadOnlyDictionary<string, string>? env = null,
        Action<string>? onOutput = null,
        string? workingDirectory = null,
        string? logTo = null);
}

public sealed class ProcessRunner : IProcessRunner
{
    public ProcessResult Run(
        string file,
        IReadOnlyList<string> args,
        IReadOnlyDictionary<string, string>? env = null,
        Action<string>? onOutput = null,
        string? workingDirectory = null,
        string? logTo = null)
    {
        var info = logTo is null
            ? new ProcessStartInfo(file)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            }
            : Redirected(file, logTo);

        info.UseShellExecute = false;
        info.WorkingDirectory = workingDirectory ?? "";

        foreach (var arg in args)
        {
            info.ArgumentList.Add(arg);
        }

        if (env is not null)
        {
            foreach (var (key, value) in env)
            {
                if (value.Length == 0)
                {
                    info.Environment.Remove(key);
                }
                else
                {
                    info.Environment[key] = value;
                }
            }
        }

        using var process = Process.Start(info)
                            ?? throw new InvalidOperationException($"could not start {file}");

        if (logTo is not null)
        {
            process.WaitForExit();

            return new ProcessResult(process.ExitCode, "", "");
        }

        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var draining = Task.WhenAll(
            Drain(process.StandardOutput, stdout, onOutput),
            Drain(process.StandardError, stderr, onOutput));

        process.WaitForExit();
        draining.GetAwaiter().GetResult();

        return new ProcessResult(process.ExitCode, stdout.ToString(), stderr.ToString());
    }

    private static ProcessStartInfo Redirected(string file, string logTo)
    {
        var info = new ProcessStartInfo("/bin/sh");

        info.ArgumentList.Add("-c");
        info.ArgumentList.Add(@"exec ""$@"" >>""$0"" 2>&1 </dev/null");
        info.ArgumentList.Add(logTo);
        info.ArgumentList.Add(file);

        return info;
    }

    private static Task Drain(StreamReader reader, TextWriter collected, Action<string>? onOutput) =>
        Task.Run(() =>
        {
            while (reader.ReadLine() is { } line)
            {
                collected.WriteLine(line);
                onOutput?.Invoke(line);
            }
        });
}
