namespace Cabinet.Contract.Tests;

public sealed class HostCommandContractTests : IDisposable
{
    private readonly Shim shim = new(bridge: false);

    public void Dispose() => shim.Dispose();

    [Fact]
    public void ADawEnrolledBeforeTheBridgeStillStartsItsSessionThroughTheHost()
    {
        var result = shim.Plugin(["exit", "3"]);

        Assert.Equal(3, result.ExitCode);
        Assert.Equal("out 3" + Environment.NewLine, result.Stdout);
        Assert.True(shim.HoppedThroughTheHost);
    }
}
