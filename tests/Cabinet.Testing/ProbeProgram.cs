namespace Cabinet.Core.Tests;

public static class ProbeProgram
{
    public static string Path { get; } = System.IO.Path.Combine(AppContext.BaseDirectory, "Cabinet.Probes.dll");
}
