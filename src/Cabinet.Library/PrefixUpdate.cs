namespace Cabinet.Core;

public sealed record PrefixUpdate(
    LibraryEntry Entry,
    string Prefix,
    string ConfigVersion,
    int Revision,
    string? Applied,
    string? Software,
    string? Revised,
    IReadOnlyList<string> Changes,
    IReadOnlyList<string> Resets,
    IReadOnlyList<string> Edits,
    IReadOnlyList<string> Present,
    IReadOnlyList<string> Dropped,
    IReadOnlyList<string> Members,
    IReadOnlyList<string> Sharing,
    IReadOnlyList<string> Winetricks,
    string Stamp)
{
    public bool Available => Changes.Count > 0 || Resets.Count > 0 || Revised is not null;

    public string Title => Available ? "Prefix update available" : "Prefix setup up to date";

    public string State => Available ? "Update available" : Edits.Count > 0 ? "Edited" : "Up to date";

    public const string DiffersFromConfig = "Differs from the config";

    public string Config => $"{ConfigVersion}, revision {Revision}";

    public string ConfigState => Available && Applied is not null && Applied != Config ? $"{Applied} → {Config}" : Config;

    public string Summary =>
        (Revised ?? $"Uses Cabinet's setup for {Entry.Name} {ConfigVersion}.")
        + (Software is null || Software == ConfigVersion ? "" : $"\nInstalled version {Software}");

    public const string WillChange = "Will be installed or changed";

    public const string ReplacesYours = "Replaces your own changes";

    public const string AlreadyInPlace = "Already in place";

    public const string NoLongerInSetup = "No longer part of the setup";

    public string? NothingToInstall => Changes.Count == 0 && Resets.Count == 0 ? "Nothing needs installing; updating records the new setup." : null;

    public string? Overrides => Resets.Count == 0 ? null : "It may override your own changes to the prefix config.";

    public string? Consent => Winetricks.Count == 0 ? null : Core.Winetricks.Consent(Winetricks);

    public string Description =>
        Summary
        + Section(WillChange, Changes)
        + Section(ReplacesYours, Resets)
        + Section(AlreadyInPlace, Present.Count == 0 ? [] : [string.Join(", ", Present)])
        + Section($"{NoLongerInSetup} (stays installed)", Dropped.Count == 0 ? [] : [string.Join(", ", Dropped)])
        + (Sharing.Count == 0 ? "" : $"\n\nThe prefix also holds {string.Join(", ", Sharing)}, from another vendor.")
        + (NothingToInstall is null ? "" : "\n\n" + NothingToInstall)
        + (Overrides is null ? "" : "\n\n" + Overrides)
        + "\n\nClose any apps or DAWs using this prefix first."
        + (Consent is null ? "" : "\n\n" + Consent);

    private static string Section(string title, IReadOnlyList<string> lines) =>
        lines.Count == 0 ? "" : $"\n\n{title}:\n" + string.Join("\n", lines.Select(line => $"- {line}"));
}
