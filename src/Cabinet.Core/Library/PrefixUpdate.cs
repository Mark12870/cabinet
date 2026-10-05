namespace Cabinet.Core;

public sealed record PrefixUpdate(
    LibraryEntry Entry,
    string Prefix,
    bool Recorded,
    string ConfigVersion,
    int Revision,
    string? Applied,
    string? Software,
    string? Revised,
    IReadOnlyList<string> Changes,
    IReadOnlyList<string> Present,
    IReadOnlyList<string> Dropped,
    IReadOnlyList<string> Preserved,
    IReadOnlyList<string> Members,
    IReadOnlyList<string> Sharing,
    IReadOnlyList<string> Winetricks,
    string Stamp)
{
    public bool Available => Changes.Count > 0 || Revised is not null;

    public string Title => Available ? "Prefix update available" : "Prefix setup up to date";

    public string Config => $"{ConfigVersion}, revision {Revision}";

    public string Summary =>
        (Revised ?? $"Uses Cabinet's setup for {Entry.Name} {ConfigVersion}.")
        + (Software is null || Software == ConfigVersion ? "" : $"\nInstalled version {Software}");

    public const string WillChange = "Will be installed or changed";

    public const string AlreadyInPlace = "Already in place";

    public const string NoLongerInSetup = "No longer part of the setup";

    public const string KeptAsSet = "Kept as you set it";

    public string? NothingToInstall => Changes.Count == 0 ? "Nothing needs installing; updating records the new setup." : null;

    public string? Unrecorded => Recorded ? null : NotUpdatedBefore(Prefix);

    public static string NotUpdatedBefore(string prefix) =>
        $"Cabinet hasn't updated {prefix} before, so it checked the prefix as it is and keeps any settings "
        + "you changed yourself.";

    public string? Consent => Winetricks.Count == 0 ? null : Core.Winetricks.Consent(Winetricks);

    public string Description =>
        Summary
        + Section(WillChange, Changes)
        + Section(AlreadyInPlace, Present.Count == 0 ? [] : [string.Join(", ", Present)])
        + Section($"{NoLongerInSetup} (stays installed)", Dropped.Count == 0 ? [] : [string.Join(", ", Dropped)])
        + Section(KeptAsSet, Preserved)
        + (Sharing.Count == 0 ? "" : $"\n\nThe prefix also holds {string.Join(", ", Sharing)}, from another vendor.")
        + (NothingToInstall is null ? "" : "\n\n" + NothingToInstall)
        + "\n\nClose any apps or DAWs using this prefix first."
        + (Unrecorded is null ? "" : "\n\n" + Unrecorded)
        + (Consent is null ? "" : "\n\n" + Consent);

    private static string Section(string title, IReadOnlyList<string> lines) =>
        lines.Count == 0 ? "" : $"\n\n{title}:\n" + string.Join("\n", lines.Select(line => $"- {line}"));
}
