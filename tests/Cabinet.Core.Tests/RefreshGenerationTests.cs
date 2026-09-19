using Cabinet.Gui;

namespace Cabinet.Core.Tests;

public sealed class RefreshGenerationTests
{
    [Fact]
    public void OnlyTheLatestGenerationRemainsCurrent()
    {
        var subject = new RefreshGeneration();

        var first = subject.Next();
        var second = subject.Next();

        Assert.False(subject.IsCurrent(first));
        Assert.True(subject.IsCurrent(second));
    }
}
