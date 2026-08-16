namespace Cabinet.Core;

public sealed record SetupReport(
    string YabridgeDir,
    string ShimPath,
    string SocketDir,
    string EnvironmentDFile,
    bool EnvironmentDWritten);

/// <summary>
/// Exports the DAW-side halves of yabridge out of the Flatpak and onto the host.
/// </summary>
/// <remarks>
/// The chainloader and <c>libyabridge-*.so</c> are loaded <em>by the DAW</em>, so they
/// cannot stay inside the sandbox; only Wine does. The Flatpak carries them purely so
/// that updating Cabinet updates them too.
/// </remarks>
public static class Setup
{
    public static SetupReport Run(Layout layout, string bundledDir = Layout.BundledYabridgeDir)
    {
        if (!Directory.Exists(bundledDir))
        {
            throw new InvalidOperationException(
                $"{bundledDir} is missing — run this from inside the Cabinet Flatpak");
        }

        Directory.CreateDirectory(layout.YabridgeDir);
        Directory.CreateDirectory(Path.GetDirectoryName(layout.ShimPath)!);
        Directory.CreateDirectory(layout.SocketDir);
        Directory.CreateDirectory(layout.PrefixesDir);

        foreach (var source in Directory.EnumerateFiles(bundledDir))
        {
            var name = Path.GetFileName(source);

            // The shim is the one file that belongs on PATH rather than beside
            // libyabridge: it is what the DAW's WINELOADER points at.
            var destination = name == "cabinet-wine"
                ? layout.ShimPath
                : Path.Combine(layout.YabridgeDir, name);

            File.Copy(source, destination, overwrite: true);
            File.SetUnixFileMode(destination, ExecutableMode(source));
        }

        var written = WriteEnvironmentD(layout);
        return new SetupReport(
            layout.YabridgeDir,
            layout.ShimPath,
            layout.SocketDir,
            layout.EnvironmentDFile,
            written);
    }

    /// <summary>
    /// Points a natively-installed DAW at the shim. Flatpak DAWs get the same three
    /// variables through <c>flatpak override</c> instead — see <see cref="Enrolment"/>.
    /// </summary>
    private static bool WriteEnvironmentD(Layout layout)
    {
        var contents = $"""
            # Written by `cabinet setup`. Points natively-installed DAWs at the shim,
            # which runs Wine inside the Cabinet Flatpak on their behalf.
            #
            # systemd reads this at login, so a DAW already running will not see it.
            WINELOADER={layout.ShimPath}
            YABRIDGE_TEMP_DIR={layout.SocketDir}
            YABRIDGE_NO_WATCHDOG=1

            """;

        if (File.Exists(layout.EnvironmentDFile)
            && File.ReadAllText(layout.EnvironmentDFile) == contents)
        {
            return false;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(layout.EnvironmentDFile)!);
        File.WriteAllText(layout.EnvironmentDFile, contents);
        return true;
    }

    private static UnixFileMode ExecutableMode(string source)
    {
        var mode = File.GetUnixFileMode(source);
        var executable = (mode & UnixFileMode.UserExecute) != 0;

        return executable
            ? UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
              | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
              | UnixFileMode.OtherRead | UnixFileMode.OtherExecute
            : UnixFileMode.UserRead | UnixFileMode.UserWrite
              | UnixFileMode.GroupRead | UnixFileMode.OtherRead;
    }
}
