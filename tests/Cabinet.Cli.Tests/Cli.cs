using Cabinet.Core;
using Cabinet.Core.Tests;

namespace Cabinet.Cli.Tests;

internal sealed record Outcome(int Exit, string Out, string Error);

internal sealed class Cli : IDisposable
{
    public const string Vendor = "a-vendor";

    private readonly string root = TestRoot.Create("cli");

    public Cli(RecordingRunner? runner = null)
    {
        Runner = runner ?? new RecordingRunner();

        Layout = new Layout(
            root,
            Path.Combine(root, "run"),
            Path.Combine(root, "data"),
            Path.Combine(root, "app"),
            Path.Combine(root, "library"),
            Path.Combine(root, "yabridge"));

        Directory.CreateDirectory(Layout.BundledYabridgeDir);
        File.WriteAllText(Path.Combine(Layout.BundledYabridgeDir, "yabridgectl"), "");
    }

    public Layout Layout { get; }

    public RecordingRunner Runner { get; }

    public Outcome Run(params string[] args) => Answer("", args);

    public Outcome Answer(string input, params string[] args)
    {
        var (output, error, entered) = (Console.Out, Console.Error, Console.In);
        using var written = new StringWriter();
        using var complained = new StringWriter();

        Console.SetOut(written);
        Console.SetError(complained);
        Console.SetIn(new StringReader(input));

        try
        {
            var exit = Program.Invoke(args, () => Layout, Runner);
            return new Outcome(exit, written.ToString(), complained.ToString());
        }
        finally
        {
            Console.SetOut(output);
            Console.SetError(error);
            Console.SetIn(entered);
        }
    }

    public void Catalogue(string id, string text)
    {
        var directory = Directory.CreateDirectory(Path.Combine(Layout.LibraryDir, Vendor));
        File.WriteAllText(Path.Combine(directory.FullName, id + ".yml"), text);
    }

    public void Prefix(string name, params string[] recorded)
    {
        Directory.CreateDirectory(Layout.PrefixPath(name));
        File.WriteAllLines(Layout.PrefixPluginsFile(name), recorded);
    }

    public void Dispose() => Directory.Delete(root, recursive: true);
}
