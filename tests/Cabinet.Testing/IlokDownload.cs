using System.IO.Compression;
using Cabinet.Core;

namespace Cabinet.Core.Tests;

public static class IlokDownload
{
    public const string Version = "1.2.3";

    public const string Folder = "LicenseSupportInstallerWin64_v" + Version + "_0a1b2c3d";

    public static readonly byte[] Package = SetupStreamWriter.Content(4000);

    public static void Serving(IReadOnlyList<string> args)
    {
        if (args is ["-fL", "--progress-bar", "--retry", "6", "-o", var target, Ilok.Url])
        {
            Write(target);
        }
    }

    public static Func<IReadOnlyList<string>, string> Answering(string state = "RUNNING") =>
        args => args is ["sc", "query", Ilok.Service] ? $"        STATE              : 4  {state}\n" : "";

    public static void Write(string zip)
    {
        using var archive = ZipFile.Open(zip, ZipArchiveMode.Create);
        using var setup = archive.CreateEntry($"{Folder}/{Ilok.Setup}").Open();
        setup.Write(SetupStreamWriter.Setup(
            SetupStreamWriter.Deflated("ISSetup.dll", SetupStreamWriter.Content(300)),
            SetupStreamWriter.Deflated(Ilok.Package, Package)));
    }
}
