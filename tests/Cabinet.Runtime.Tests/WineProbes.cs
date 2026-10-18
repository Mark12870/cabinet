using Cabinet.Core.Tests;

namespace Cabinet.Runtime.Tests;

internal static class WineProbes
{
    private static readonly Lazy<string> Built = new(Build);

    public static string Path(string name) => System.IO.Path.Combine(Built.Value, name + ".exe");

    private static string Build()
    {
        var target = System.IO.Path.Combine(RuntimeTestEnvironment.TemporaryDirectory, "wine-probes");
        var result = Host.Run(
            "cargo",
            [
                "build",
                "--release",
                "--target",
                "x86_64-pc-windows-gnu",
                "--manifest-path",
                Repo.Path("tools/wine-probes/Cargo.toml"),
                "--target-dir",
                target,
            ]);

        return result.ExitCode == 0
            ? System.IO.Path.Combine(target, "x86_64-pc-windows-gnu", "release")
            : throw new InvalidOperationException("could not build the Wine probes: " + result.Error);
    }
}
