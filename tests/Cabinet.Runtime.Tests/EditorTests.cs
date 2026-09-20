using System.Text.RegularExpressions;

namespace Cabinet.Runtime.Tests;

public sealed class EditorTests : IDisposable
{
    private readonly RuntimeTestLock runtimeLock = RuntimeTestLock.Acquire();

    [Theory]
    [InlineData("vst2", ".vst", "ValhallaSupermassive_x64.so")]
    [InlineData("vst2", ".vst", "Sitala.so")]
    [InlineData("vst3", ".vst3", "SINE Player.vst3")]
    public void AnEditorDrawsWhereTheWindowHostingItIs(string format, string root, string plugin)
    {
        var seen = Measure(format, root, plugin);

        Assert.True(
            seen.Wrapper == seen.Wine,
            $"{plugin}'s editor is at {seen.Wine}, not at {seen.Wrapper} where the window hosting "
            + $"it is. It is drawn {seen.Wine.X - seen.Wrapper.X} across and "
            + $"{seen.Wine.Y - seen.Wrapper.Y} down from the frame the DAW shows, so that frame is "
            + "blank. See the yabridge source note in PATCHES.md.");
    }

    [Theory]
    [InlineData("vst2", ".vst", "ValhallaSupermassive_x64.so")]
    [InlineData("vst2", ".vst", "Sitala.so")]
    [InlineData("vst3", ".vst3", "SINE Player.vst3")]
    public void AnEditorIsWhereWineHasBeenToldItIs(string format, string root, string plugin)
    {
        var seen = Measure(format, root, plugin);

        Assert.True(
            seen.Told == seen.Wine,
            $"Wine places {plugin}'s editor at {seen.Wine} but has been told it is at {seen.Told}. "
            + $"It turns a screen coordinate into a client one with what it was told, so every "
            + $"click lands {seen.Wine.X - seen.Told.X} across and {seen.Wine.Y - seen.Told.Y} "
            + "down from the pointer. See the yabridge source note in PATCHES.md.");
    }

    private static Seen Measure(string format, string root, string plugin)
    {
        var said = EditorProbe.Run(
            "editor-geometry.py",
            Path.GetFileNameWithoutExtension(plugin),
            Fixtures.Windows(root, plugin),
            format).Said;

        var seen = Regex.Match(
            said,
            @"WRAPPER=\((-?\d+), (-?\d+)\) WINE=\((-?\d+), (-?\d+)\) TOLD=\((-?\d+), (-?\d+)\)");

        Assert.True(seen.Success, $"the editor probe did not report a geometry: {said}");

        int At(int group) => int.Parse(seen.Groups[group].Value);

        return new Seen(
            (At(1), At(2)),
            (At(3), At(4)),
            (At(5), At(6)));
    }

    private sealed record Seen(
        (int X, int Y) Wrapper, (int X, int Y) Wine, (int X, int Y) Told);

    public void Dispose() => runtimeLock.Dispose();
}
