namespace Cabinet.Gui;

internal sealed record Approval(
    string Heading, string Body, string Action, Adw.ResponseAppearance Appearance);
