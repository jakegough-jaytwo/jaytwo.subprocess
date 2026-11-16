using System.IO;
using Xunit;

namespace jaytwo.Subprocess.UnitTests;

/// <summary>
/// A fact that is reported as skipped (not passed) on Windows. Plain xunit rather than Xunit.SkippableFact, which
/// mis-reports discovery on netcoreapp3.1.
/// </summary>
internal sealed class PosixFactAttribute : FactAttribute
{
    public PosixFactAttribute()
    {
        if (Path.DirectorySeparatorChar != '/')
        {
            Skip = "Requires /bin/sh";
        }
    }
}
