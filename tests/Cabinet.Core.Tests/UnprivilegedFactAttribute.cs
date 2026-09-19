namespace Cabinet.Core.Tests;

public sealed class UnprivilegedFactAttribute : FactAttribute
{
    public UnprivilegedFactAttribute()
    {
        if (Environment.IsPrivilegedProcess)
        {
            Skip = "root ignores the file modes this test relies on";
        }
    }
}
