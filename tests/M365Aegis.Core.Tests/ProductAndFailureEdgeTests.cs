using System.Reflection;
using M365Aegis.Core.Output;

namespace M365Aegis.Core.Tests;

/// <summary>The plain version of an assembly's attributes and the failure an exception carries, at their edges.</summary>
public sealed class ProductAndFailureEdgeTests
{
    [Theory]
    [InlineData("0.1.0+0123abc", null, "0.1.0")]
    [InlineData("0.1.0", null, "0.1.0")]
    [InlineData("", "1.2.3.4", "1.2.3")]
    [InlineData(null, "1.2.3.4", "1.2.3")]
    [InlineData(null, null, "0.0.0")]
    public void TheVersionIsTheInformationalOneWithoutTheCommitElseTheAssemblyVersion(string? informational, string? version, string expected)
    {
        AssemblyInformationalVersionAttribute? attribute = informational is null ? null : new(informational);

        Assert.Equal(expected, ProductInfo.PlainVersion(attribute, version is null ? null : Version.Parse(version)));
    }

    [Fact]
    public void TheProductVersionIsPlain()
    {
        Assert.DoesNotContain("+", ProductInfo.Version, StringComparison.Ordinal);
    }

    [Fact]
    public void AnExceptionOfNoFailureIsRefused()
    {
        Assert.Throws<ArgumentNullException>(() => new AegisException((AegisFailure)null!));
    }
}
