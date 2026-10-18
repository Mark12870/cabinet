using System.Diagnostics;

namespace Cabinet.Runtime.Tests.Patches;

public sealed class DragAndDropTests : IDisposable
{
    private readonly RuntimeTestLock runtimeLock = RuntimeTestLock.Acquire();
    private static readonly TimeSpan Patience = TimeSpan.FromMinutes(3);

    [Theory]
    [InlineData("drag-drop-bundled")]
    [InlineData("drag-drop-d2d1")]
    [InlineData("drag-drop-kron4ek")]
    [InlineData("drag-drop-soda")]
    public void RevokingDragAndDropOnAWindowOfAnotherProcessStillCrashesTheNewestRunner(string prefix)
    {
        var session = Path.Combine(RuntimeTestEnvironment.RuntimeDirectory, "yabridge", "dd");

        try
        {
            var said = RunProbe(prefix, session);

            Assert.Matches("revoke (crashed|survived|hung)", said);
            Assert.True(
                said.Contains("revoke crashed", StringComparison.Ordinal),
                $"{RunnerOf(prefix)} no longer crashes when one process revokes drag-and-drop on another "
                + "process's window. Move the catalogue entries onto it; once no case here crashes, "
                + $"patches/yabridge/yabridge-foreign-drag-drop.patch is no longer needed (see PATCHES.md). The probe said: {said}");
        }
        finally
        {
            Host.KillAll(Host.App, session);
            Host.Discard(session);
        }
    }

    public void Dispose() => runtimeLock.Dispose();

    private static string PrefixPath(string prefix) =>
        Path.Combine(RuntimeTestEnvironment.Home, ".var", "app", Host.App, "data", "prefixes", prefix);

    private static string RunnerOf(string prefix)
    {
        var marker = Path.Combine(PrefixPath(prefix), ".cabinet-runner");
        return File.Exists(marker) ? File.ReadAllText(marker).Trim() : "the bundled Wine";
    }

    private static string RunProbe(string prefix, string session)
    {
        var info = new ProcessStartInfo(Host.Shim())
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        Host.Configure(info);

        info.Environment["WINEPREFIX"] = PrefixPath(prefix);
        info.Environment["YABRIDGE_TEMP_DIR"] = session;
        info.ArgumentList.Add(WineProbes.Path("revoke-drag-drop"));

        using var process = Process.Start(info) ?? throw new InvalidOperationException("could not start the shim");
        process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEndAsync();
        _ = process.StandardError.ReadToEndAsync();

        if (!process.WaitForExit(Patience))
        {
            process.Kill(entireProcessTree: true);
        }

        Host.KillAll(Host.App, session);
        return output.Wait(Patience) ? output.Result : "(no output before the timeout)";
    }
}
