using System.Text;

namespace Cabinet.Core;

public sealed class InstallScript(Layout layout, IProcessRunner runner)
{
    public void Run(
        LibraryEntry entry,
        string archive,
        string work,
        string where,
        IReadOnlyDictionary<string, string> variables,
        Action<string>? onOutput)
    {
        var environment = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["CABINET_ARCHIVE"] = archive,
            ["CABINET_WORK"] = work,
            ["CABINET_ID"] = entry.Id,
            ["CABINET_NAME"] = entry.Name,
        };

        foreach (var (key, value) in variables)
        {
            environment[key] = value;
        }

        Directory.CreateDirectory(work);
        Execute(entry, entry.Script!, environment, where, onOutput);
    }

    public void Recover(
        LibraryEntry entry,
        string where,
        string kept,
        IReadOnlyDictionary<string, string> variables,
        Action<string>? onOutput)
    {
        var environment = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["CABINET_KEPT"] = kept,
            ["CABINET_ID"] = entry.Id,
            ["CABINET_NAME"] = entry.Name,
        };

        foreach (var (key, value) in variables)
        {
            environment[key] = value;
        }

        Execute(entry, entry.Recover!, environment, where, onOutput, announce: false);
    }

    private void Execute(
        LibraryEntry entry,
        string name,
        IReadOnlyDictionary<string, string> environment,
        string where,
        Action<string>? onOutput,
        bool announce = true)
    {
        var script = layout.LibraryScript(entry.Vendor, name);

        if (!File.Exists(script))
        {
            throw new FileNotFoundException(
                $"{entry.Name} uses {name}, which this build did not ship",
                script);
        }

        if (announce)
        {
            onOutput?.Invoke($"Running {name}");
        }

        var result = Detached(entry.Id, script, environment, where, onOutput);

        if (!result.Ok)
        {
            throw new InvalidOperationException($"{name} exited with {result.ExitCode}");
        }
    }

    private static readonly TimeSpan Beat = TimeSpan.FromMilliseconds(200);

    private ProcessResult Detached(
        string id,
        string script,
        IReadOnlyDictionary<string, string> environment,
        string where,
        Action<string>? onOutput)
    {
        var log = Layout.Staging($"{id}-{Environment.ProcessId}-script.log");
        File.WriteAllText(log, "");

        using var ended = new CancellationTokenSource();
        var read = 0L;
        var following = Task.Run(() =>
        {
            while (!ended.Token.WaitHandle.WaitOne(Beat))
            {
                read = Follow(log, read, onOutput);
            }
        });

        try
        {
            return runner.Run("sh", ["-e", script], environment, null, where, log);
        }
        finally
        {
            ended.Cancel();
            following.Wait();
            Follow(log, read, onOutput, ending: true);
            File.Delete(log);
        }
    }

    private static long Follow(
        string log, long from, Action<string>? onOutput, bool ending = false)
    {
        using var file = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        file.Seek(from, SeekOrigin.Begin);
        using var reading = new StreamReader(file);
        var text = reading.ReadToEnd();
        var complete = ending ? text.Length : text.LastIndexOf('\n') + 1;

        if (complete == 0)
        {
            return from;
        }

        foreach (var line in text[..complete].TrimEnd('\n').Split('\n'))
        {
            onOutput?.Invoke(line);
        }

        return from + Encoding.UTF8.GetByteCount(text[..complete]);
    }
}
