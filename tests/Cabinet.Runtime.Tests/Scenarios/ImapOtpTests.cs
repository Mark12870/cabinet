namespace Cabinet.Runtime.Tests.Scenarios;

public sealed class ImapOtpTests
{
    [Theory]
    [InlineData("From: Cymatics <no-reply@example.com>\nYour code is 123456", "123456")]
    [InlineData("From: Cymatics <no-reply@example.com>\n123456 is your code", "123456")]
    [InlineData("From: Cymatics <no-reply@example.com>\nOrder 654321\nYour verification code is 123456", "123456")]
    [InlineData("From: Cymatics <no-reply@example.com>\n<p style=\"color:#333333\">Your verification code</p> 123456", "123456")]
    [InlineData("From: Cymatics <no-reply@example.com>\nUse 123456 to continue", "123456")]
    [InlineData("From: Cymatics <no-reply@example.com>\nUse 1234567 to continue", null)]
    [InlineData("From: Other service <no-reply@example.com>\nCymatics\nUse 123456 to continue", null)]
    public void ExtractsOnlySixDigitCodesFromTheSendersMessages(string message, string? expected)
    {
        Assert.Equal(expected, ImapOtp.CodeFromMessage(message, "Cymatics"));
    }
}
