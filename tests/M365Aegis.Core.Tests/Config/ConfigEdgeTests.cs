using M365Aegis.Core.Config;

namespace M365Aegis.Core.Tests.Config;

/// <summary>The home folder without the override, and the profile file's odd shapes.</summary>
public sealed class ConfigEdgeTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public void WithoutTheOverrideTheHomeIsUnderTheLocalApplicationData(string? overridden)
    {
        // Resolved only, never created: the real home folder is not touched.
        string expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ProductInfo.Name);

        Assert.Equal(expected, AegisHome.ResolveFrom(overridden));
    }

    [Fact]
    public void TheHomeAboveAFolderIsItsParentAndARootIsItsOwnHome()
    {
        string home = ProfileStoreTests.TempHome();
        string root = Path.GetPathRoot(Path.GetTempPath())!;

        Assert.Equal(Path.GetFullPath(home), AegisHome.CreateAbove(Path.Combine(home, "logs")));
        Assert.True(Directory.Exists(home));
        // A folder that exists already is left as it is.
        Assert.Equal(Path.GetFullPath(home), AegisHome.Create(home));
        Assert.Equal(root, AegisHome.CreateAbove(root));
    }

    [Fact]
    public void TheOverrideIsMadeAbsolute()
    {
        string home = ProfileStoreTests.TempHome();

        Assert.Equal(Path.GetFullPath(home), AegisHome.ResolveFrom(home));
    }

    [Fact]
    public void AFileOfJsonNullHoldsNoProfile()
    {
        string home = WithConfig("null");

        ProfileList list = new ProfileStore(home).Describe();

        Assert.Empty(list.Profiles);
        Assert.Null(list.Default);
    }

    [Fact]
    public void ADefaultThatNamesNoProfileMarksNone()
    {
        string home = WithConfig("""
            {"default":"gone","profiles":{
              "work":{"tenant":"contoso.onmicrosoft.com","clientId":"00000000-0000-0000-0000-000000000010","account":"alice@contoso.com"}}}
            """);

        ProfileList list = new ProfileStore(home).Describe();

        Assert.Null(list.Default);
        Assert.False(Assert.Single(list.Profiles).IsDefault);
    }

    private static string WithConfig(string text)
    {
        string home = ProfileStoreTests.TempHome();
        Directory.CreateDirectory(home);
        File.WriteAllText(Path.Combine(home, "config.json"), text);
        return home;
    }
}
