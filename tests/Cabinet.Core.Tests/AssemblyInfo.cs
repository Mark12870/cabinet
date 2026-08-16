using System.Runtime.Versioning;

// Cabinet is a Flatpak wrapping Wine and yabridge. Declaring the platform lets the
// analyzer accept the Unix file-mode and /proc reads it would otherwise flag, without
// suppressing CA1416 case by case.
[assembly: SupportedOSPlatform("linux")]
