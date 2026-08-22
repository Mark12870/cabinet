namespace Cabinet.Core.Tests;

public static class Repo
{
    public static string Root { get; } = Find();

    public static string Path(string relative) =>
        System.IO.Path.Combine(Root, relative);

    public static string Read(string relative) =>
        File.ReadAllText(Path(relative));

    public static string[] Lines(string relative) =>
        File.ReadAllLines(Path(relative));

    private static string Find()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null)
        {
            if (File.Exists(System.IO.Path.Combine(dir.FullName, "Cabinet.slnx")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(
            $"no Cabinet.slnx above {AppContext.BaseDirectory}");
    }
}
