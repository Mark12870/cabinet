using System.ComponentModel;
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
        string? logTo = null,
        IReadOnlySet<string>? blankEnvironment = null,
        bool interactive = false,
        CancellationToken cancellationToken = default);
}

public sealed class ProcessRunner : IProcessRunner
{
    public ProcessResult Run(
        string file,
        IReadOnlyList<string> args,
        IReadOnlyDictionary<string, string>? env = null,
        Action<string>? onOutput = null,
        string? workingDirectory = null,
        string? logTo = null,
        IReadOnlySet<string>? blankEnvironment = null,
        bool interactive = false,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var info = logTo is null
            ? new ProcessStartInfo(file)
            {
                RedirectStandardOutput = !interactive,
                RedirectStandardError = !interactive,
                RedirectStandardInput = !interactive,
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

        if (blankEnvironment is not null)
        {
            foreach (var key in blankEnvironment)
            {
                info.Environment[key] = "";
            }
        }

        using var process = Process.Start(info)
                            ?? throw new InvalidOperationException($"could not start {file}");
        using var cancellation = cancellationToken.Register(static state => Terminate((Process)state!), process);

        if (info.RedirectStandardInput)
        {
            process.StandardInput.Close();
        }

        if (logTo is not null || interactive)
        {
            process.WaitForExit();
            cancellationToken.ThrowIfCancellationRequested();

            return new ProcessResult(process.ExitCode, "", "");
        }

        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var draining = Task.WhenAll(
            Drain(process.StandardOutput, stdout, onOutput),
            Drain(process.StandardError, stderr, onOutput));

        process.WaitForExit();
        draining.GetAwaiter().GetResult();
        cancellationToken.ThrowIfCancellationRequested();

        return new ProcessResult(process.ExitCode, stdout.ToString(), stderr.ToString());
    }

    private static void Terminate(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
        }
        catch (Win32Exception)
        {
        }
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
            var line = new StringWriter();
            var afterCarriageReturn = false;

            while (reader.Read() is var value && value >= 0)
            {
                var character = (char)value;
                collected.Write(character);

                if (character == '\n' && afterCarriageReturn)
                {
                    afterCarriageReturn = false;
                    continue;
                }

                if (character is '\r' or '\n')
                {
                    onOutput?.Invoke(line.ToString());
                    line.GetStringBuilder().Clear();
                    afterCarriageReturn = character == '\r';
                }
                else
                {
                    line.Write(character);
                    afterCarriageReturn = false;
                }
            }

            if (line.GetStringBuilder().Length > 0)
            {
                onOutput?.Invoke(line.ToString());
            }
        });
}
