using System.Collections.Concurrent;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Cabinet.Runtime.Tests;

public sealed class InteractionTests : IDisposable
{
    private readonly RuntimeTestLock runtimeLock = RuntimeTestLock.Acquire();

    private const int Colours = 16;
    private const double Reaction = 0.0005;
    private const int Steady = 250;
    private const int Stalling = 2500;
    private const int CallMilliseconds = 10000;

    private static readonly ConcurrentDictionary<string, Lazy<Measured>> Seen = new();

    private static readonly EditorCase[] Catalogue =
    [
        new("decent-sampler", "vst2", Fixtures.Native(".vst", "DecentSampler.so"), Stalling),
        new("surge-xt", "clap", Fixtures.Native(".clap", "Surge XT.clap"), Steady),
        new("sitala", "vst2", Fixtures.Windows(".vst", "Sitala.so"), Steady),
        new("valhalla-supermassive", "vst2",
            Fixtures.Windows(".vst", "ValhallaSupermassive_x64.so"), Steady),
        new("valhalla-supermassive-vst3", "vst3",
            Fixtures.Windows(".vst3", "ValhallaSupermassive.vst3"), Steady),
        new("fabfilter-micro", "clap", Fixtures.Windows(".clap", "FabFilter Micro.clap"), Steady),
        new("sine-player", "vst3", Fixtures.Windows(".vst3", "SINE Player.vst3"), Steady),
    ];

    public static IEnumerable<object[]> EveryCase() =>
        Catalogue.Select(plugin => new object[] { plugin });

    [Theory]
    [MemberData(nameof(EveryCase))]
    public void EveryFrameOfAnEditorIsMeasured(EditorCase plugin)
    {
        var seen = Measure(plugin);

        Assert.True(
            seen.Blind == 0,
            $"{seen.Blind} captures of {plugin.Name}'s editor came back empty, so the probe was "
            + "blind for part of the sweep and every number it reports for those frames is a "
            + $"zero it never measured. The captures are in {seen.Shots}.");
    }

    [Theory]
    [MemberData(nameof(EveryCase))]
    public void AnEditorDrawsItsInterface(EditorCase plugin)
    {
        var seen = Measure(plugin);

        Assert.True(
            seen.Colours >= Colours,
            $"{plugin.Name}'s editor window is {seen.Size} but holds only {seen.Colours} distinct "
            + "colours, so the frame the DAW shows is blank. The plugin may be drawing somewhere "
            + $"else entirely; no log records this. The capture is in {seen.Shots}.");
    }

    [Theory]
    [MemberData(nameof(EveryCase))]
    public void AnEditorRespondsToThePointer(EditorCase plugin)
    {
        var seen = Measure(plugin);

        if (seen.Closed)
        {
            return;
        }

        Assert.True(
            seen.Reaction > seen.Noise && seen.Reaction >= Reaction,
            $"{plugin.Name}'s editor draws but does not react: sweeping, clicking and dragging "
            + $"across it changed the frame by {seen.Reaction:F6}, against {seen.Noise:F6} measured "
            + "with the pointer held still. The editor is receiving no input, which is a plugin a "
            + $"user cannot operate. It is {seen.Size} and holds {seen.Colours} colours, and "
            + $"{seen.Blind} of its captures were blind. The captures are in {seen.Shots}.");
    }

    [Theory]
    [MemberData(nameof(EveryCase))]
    public void AHostKeepsRespondingWhileAnEditorIsOpen(EditorCase plugin)
    {
        var seen = Measure(plugin);

        Assert.True(
            seen.Idle <= plugin.Idle,
            $"the host stopped servicing its loop for {seen.Idle} ms while {plugin.Name}'s editor "
            + $"was open, against the {plugin.Idle} ms this one is allowed. That is the DAW going "
            + "unresponsive under the user's hands.");

        Assert.True(
            seen.Slowest <= CallMilliseconds,
            $"{plugin.Name} held the host for {seen.Slowest} ms in one call (open {seen.Open} ms, "
            + $"close {seen.Close} ms, remove {seen.Remove} ms, shutdown {seen.Shutdown} ms). "
            + "A stall in shutdown is the DAW freeze described in AGENTS.md.");
    }

    [Theory]
    [MemberData(nameof(EveryCase))]
    public void AnEditorSurvivesBeingOpenedAndClosed(EditorCase plugin)
    {
        var seen = Measure(plugin);

        Assert.True(
            seen.Reopened,
            $"{plugin.Name}'s editor did not come back when it was opened a second time, so the "
            + "user gets one look at it per session.");

        Assert.DoesNotContain("crashed while being torn down", seen.Said, StringComparison.Ordinal);
    }

    private static Measured Measure(EditorCase plugin) =>
        Seen.GetOrAdd(plugin.Name, _ => new Lazy<Measured>(() => Probe(plugin))).Value;

    private static Measured Probe(EditorCase plugin)
    {
        var shots = EditorProbe.Artefacts(plugin.Name);
        var said = EditorProbe.Run(
            "editor-interaction.py", plugin.Name, plugin.Plugin, plugin.Format, shots).Said;

        File.WriteAllText(Path.Combine(shots, "probe.log"), said);

        var seen = Regex.Match(
            said,
            @"EDITOR=(\S+) SIZE=(\S+) COLOURS=(\d+) REACTION=([0-9.]+) NOISE=([0-9.]+) "
            + @"IDLE_MS=(\d+) OPEN_MS=(\d+) CLOSE_MS=(\d+) REMOVE_MS=(\d+) SHUTDOWN_MS=(\d+) "
            + @"BLIND=(\d+) CLOSED=(yes|no) REOPEN=(yes|no)");

        Assert.True(
            seen.Success,
            $"the interaction probe did not report a measurement for {plugin.Name}: {said}");

        int Count(int group) => int.Parse(seen.Groups[group].Value, CultureInfo.InvariantCulture);
        double Ratio(int group) =>
            double.Parse(seen.Groups[group].Value, CultureInfo.InvariantCulture);

        return new Measured(
            seen.Groups[2].Value,
            Count(3),
            Ratio(4),
            Ratio(5),
            Count(6),
            Count(7),
            Count(8),
            Count(9),
            Count(10),
            Count(11),
            seen.Groups[12].Value == "yes",
            seen.Groups[13].Value == "yes",
            shots,
            said);
    }

    public void Dispose() => runtimeLock.Dispose();
}

public sealed record EditorCase(string Name, string Format, string Plugin, int Idle)
{
    public override string ToString() => Name;
}

internal sealed record Measured(
    string Size,
    int Colours,
    double Reaction,
    double Noise,
    int Idle,
    int Open,
    int Close,
    int Remove,
    int Shutdown,
    int Blind,
    bool Closed,
    bool Reopened,
    string Shots,
    string Said)
{
    public int Slowest => Math.Max(Math.Max(Open, Close), Math.Max(Remove, Shutdown));
}
