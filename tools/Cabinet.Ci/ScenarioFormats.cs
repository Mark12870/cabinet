namespace Cabinet.Ci;

internal static class ScenarioFormats
{
    private static readonly string[] Main = ["VST3", "LV2"];

    public static IReadOnlyList<string> Tested(IReadOnlyList<string> declared) =>
        [.. declared.Where(format => Main.Contains(format) || format == "VST2" && !declared.Any(Main.Contains))];
}
