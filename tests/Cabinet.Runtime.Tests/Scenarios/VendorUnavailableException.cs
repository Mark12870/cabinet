namespace Cabinet.Runtime.Tests.Scenarios;

internal sealed class VendorUnavailableException(string reason) : Exception(reason);
