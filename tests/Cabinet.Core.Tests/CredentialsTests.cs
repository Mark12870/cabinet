using Cabinet.Runtime.Tests.Scenarios;

namespace Cabinet.Core.Tests;

public sealed class CredentialsTests
{
    [Fact]
    public void ReadPreservesValuesContainingEquals()
    {
        var credentials = Credentials.ReadFrom(
            ["EMAIL = synthetic@example.test ", "PASSWORD=synthetic=secret"],
            "EMAIL",
            "PASSWORD");

        Assert.Equal("synthetic@example.test", credentials["EMAIL"]);
        Assert.Equal("synthetic=secret", credentials["PASSWORD"]);
    }

    [Fact]
    public void ReadNamesMissingAndBlankCredentialsWithoutIncludingValues()
    {
        var error = Assert.Throws<InvalidOperationException>(() => Credentials.ReadFrom(
            ["EMAIL=synthetic@example.test", "EMAIL_IMAP_HOST= ", "EMAIL_IMAP_PASSWORD=synthetic-secret"],
            "EMAIL_IMAP_HOST",
            "EMAIL",
            "EMAIL_IMAP_PASSWORD",
            "PASSWORD"));

        Assert.Equal("Required credentials are missing or blank: EMAIL_IMAP_HOST, PASSWORD.", error.Message);
        Assert.DoesNotContain("synthetic@example.test", error.Message);
        Assert.DoesNotContain("synthetic-secret", error.Message);
    }
}
