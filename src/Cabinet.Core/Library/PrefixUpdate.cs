namespace Cabinet.Core;

public sealed record PrefixUpdate(
    LibraryEntry Entry,
    string Prefix,
    bool Recorded,
    string Config,
    string? Applied,
    string? Software,
    IReadOnlyList<string> Changes,
    IReadOnlyList<string> Preserved,
    IReadOnlyList<string> Members,
    IReadOnlyList<string> Sharing,
    string Stamp)
{
    public bool Available => Changes.Count > 0;

    public string Title => Available ? "Prefix update available" : "Prefix setup up to date";

    public string Description =>
        $"Apply prefix config {Config} to {Prefix}."
        + (Software is null ? "" : $" The installed software is version {Software}.")
        + (Applied is null ? "" : $" The prefix has config {Applied}.")
        + (Recorded ? "" : " Its earlier config was not recorded, so this compares the prefix itself.")
        + (Members.Count < 2 ? "" : $"\n\nThe prefix holds {string.Join(", ", Members)}, which share this config.")
        + (Changes.Count == 0 ? "" : "\n\n" + string.Join("\n", Changes))
        + (Preserved.Count == 0 ? "" : "\n\nSettings kept:\n" + string.Join("\n", Preserved))
        + (Sharing.Count == 0 ? "" : $"\n\nThis prefix also holds {string.Join(", ", Sharing)}, from another config.")
        + "\n\nClose any apps or DAWs using this prefix before updating."
        + (Entry.Winetricks.Count == 0 ? "" : "\n\n" + Winetricks.Consent(Entry.Winetricks));
}
