namespace Cabinet.Runtime.Tests;

internal static class Fixtures
{
    public static string Native(string extension, string name) =>
        extension == ".lv2"
            ? Path.Combine(RuntimeTestEnvironment.Home, extension, name)
            : Path.Combine(RuntimeTestEnvironment.Home, extension, "cabinet", "native", name);

    public static string Windows(string extension, string name) =>
        Path.Combine(RuntimeTestEnvironment.Home, extension, "cabinet", "windows", name);
}
