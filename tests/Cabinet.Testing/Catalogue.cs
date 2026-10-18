using Cabinet.Core;

namespace Cabinet.Core.Tests;

public static class Catalogue
{
    public static Layout Layout() =>
        new("/nonexistent", "/nonexistent", "/nonexistent", "/nonexistent",
            Repo.Path("data/library"));

    public static IReadOnlyList<LibraryEntry> Entries() =>
        new Library(Layout(), new UnusedRunner()).Entries();
}
