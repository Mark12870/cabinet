namespace Cabinet.Runtime.Tests;

public sealed class WinelibHostTests : IDisposable
{
    private readonly RuntimeTestLock runtimeLock = RuntimeTestLock.Acquire();
    private readonly string prefix = Path.Combine(RuntimeTestEnvironment.TemporaryDirectory, "winelib-host");

    public WinelibHostTests() => Host.Discard(prefix);

    [Theory]
    [InlineData("yabridge-host.exe.so", "yabridge host version ")]
    [InlineData("yabridge-host-32.exe.so", "(32-bit compatibility mode)")]
    public void AWinelibHostLoadsUnderTheBundledWineAndReachesItsOwnCode(string host, string banner)
    {
        var result = Host.Run(
            "flatpak",
            [
                "run",
                "--command=sh",
                $"--filesystem={RuntimeTestEnvironment.Root}:create",
                $"--env=WINEPREFIX={prefix}",
                "--env=WINEDEBUG=err+module",
                "--env=WINEDLLOVERRIDES=mscoree=d;mshtml=d",
                Host.App,
                "-c",
                $"wine /app/lib/yabridge/{host}; status=$?; wineserver -w; exit $status",
            ]);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains(banner, result.Output + result.Error);
        Assert.DoesNotContain("err:module", result.Output + result.Error);
    }

    public void Dispose()
    {
        Host.Discard(prefix);
        runtimeLock.Dispose();
    }
}
