using Cabinet.Core;

namespace Cabinet.Core.Tests;

public sealed class PrefixSearchTests
{
    private static readonly Prefix Tuned =
        new("gadget", "/prefixes/gadget", true, "wine-11.0", "2.6", "5.9.0", SyncMode.Ntsync, false);

    private static readonly Prefix Bare =
        new("widget", "/prefixes/widget", false, "wine-11.0", null, null, SyncMode.System, false);

    [Fact]
    public void TraitsAreWhatThePrefixRowShows()
    {
        Assert.Equal(["wine-11.0", "ntsync", "DXVK 2.6", "iLok 5.9.0"], Tuned.Traits);
        Assert.Equal(["not initialised"], Bare.Traits);
    }

    [Fact]
    public void EveryWordMustAppearInTheName()
    {
        Assert.True(Tuned.Matches(null));
        Assert.True(Tuned.Matches("  "));
        Assert.True(Tuned.Matches("GADGET"));
        Assert.True(Tuned.Matches("gad get"));
        Assert.False(Tuned.Matches("ntsync"));
        Assert.False(Tuned.Matches("gadget wine"));
        Assert.False(Bare.Matches("not initialised"));
    }
}
