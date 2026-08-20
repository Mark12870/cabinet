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
        var script = layout.LibraryScript(entry.Vendor, entry.Script!);

        if (!File.Exists(script))
        {
            throw new FileNotFoundException(
                $"{entry.Name} installs with {entry.Script}, which this build did not ship",
                script);
        }

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
        onOutput?.Invoke($"Installing with {entry.Script}");

        var result = runner.Run("sh", ["-e", script], environment, onOutput, where);

        if (!result.Ok)
        {
            throw new InvalidOperationException($"{entry.Script} exited with {result.ExitCode}");
        }
    }
}
