using System.Runtime.CompilerServices;

namespace Cabinet.Core.Tests;

public sealed class UnprivilegedFactAttribute : FactAttribute
{
    public UnprivilegedFactAttribute(
        [CallerFilePath] string? sourceFilePath = null,
        [CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber)
    {
        if (Environment.IsPrivilegedProcess)
        {
            Skip = "root ignores the file modes this test relies on";
        }
    }
}
