namespace FileOps.Core.Tests.Support;

/// <summary>
/// A fact that only runs as an unprivileged user. Root bypasses file permissions, so permission-denied
/// tests would fail inside CI containers that run as root.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
internal sealed class NonRootFactAttribute : FactAttribute
{
    public NonRootFactAttribute()
    {
        if (Environment.IsPrivilegedProcess)
        {
            Skip = "Requires an unprivileged user: root ignores file permissions.";
        }
        else if (OperatingSystem.IsWindows())
        {
            Skip = "Unix file modes are not available on Windows.";
        }
    }
}
