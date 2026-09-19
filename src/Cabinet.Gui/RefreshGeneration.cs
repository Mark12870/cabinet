namespace Cabinet.Gui;

internal sealed class RefreshGeneration
{
    private int current;

    public int Next() => ++current;

    public bool IsCurrent(int generation) => generation == current;
}
