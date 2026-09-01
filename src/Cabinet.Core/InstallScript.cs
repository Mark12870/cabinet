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

        var result = runner.Run("sh", ["-e", script], environment, onOutput, where);

        if (!result.Ok)
        {
            throw new InvalidOperationException($"{name} exited with {result.ExitCode}");
        }
    }
}
