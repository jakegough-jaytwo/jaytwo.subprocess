using System.Reflection;
using System.Runtime.Versioning;
using Xunit;

namespace jaytwo.Subprocess.UnitTests;

public class LibraryTargetFrameworkTests
{
    [Fact]
    public void Library_targets_netstandard2_0()
    {
        // arrange
        var assembly = typeof(CliCommand).Assembly;

        // act
        var targetFramework = assembly.GetCustomAttribute<TargetFrameworkAttribute>();

        // assert
        Assert.NotNull(targetFramework);
        Assert.Equal(".NETStandard,Version=v2.0", targetFramework!.FrameworkName);
    }
}
