namespace Cabinet.Core;

public sealed record Runner(string Name, string Wine, bool Bundled)
{
    public bool Usable => Bundled || File.Exists(Wine);
}

public sealed class Runners(Layout layout)
{
    public IReadOnlyList<Runner> List()
    {
        var runners = new List<Runner> { Bundled };

        if (!Directory.Exists(layout.RunnersDir))
        {
            return runners;
        }

        runners.AddRange(Directory.EnumerateDirectories(layout.RunnersDir)
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => Path.GetFileName(path))
            .Where(name => name != Layout.BundledRunner)
            .Select(name => new Runner(name, layout.RunnerWine(name), Bundled: false)));

        return runners;
    }

    public Runner Bundled => new(Layout.BundledRunner, Layout.Wine, Bundled: true);

    public Runner Resolve(string? name)
    {
        if (string.IsNullOrEmpty(name) || name == Layout.BundledRunner)
        {
            return Bundled;
        }

        var runner = new Runner(name, layout.RunnerWine(name), Bundled: false);

        if (!runner.Usable)
        {
            throw new InvalidOperationException(
                $"runner '{name}' has no {Path.Combine("bin", "wine")} — "
                + $"unpack one into {layout.RunnerPath(name)}");
        }

        return runner;
    }
}
