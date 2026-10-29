using M365Aegis.Core.Config;
using M365Aegis.Core.Output;

namespace M365Aegis.Core.Tests.Config;

/// <summary>The tenant profiles in config.json under a temporary home folder.</summary>
public sealed class ProfileStoreTests
{
    private static readonly TenantProfile Work = new("work", "contoso.onmicrosoft.com", "00000000-0000-0000-0000-000000000010", "alice@contoso.com");

    [Fact]
    public void NoFileIsAnAuthFailureWithTheLoginCommand()
    {
        var store = new ProfileStore(TempHome());

        AegisException exception = Assert.Throws<AegisException>(() => store.Resolve(null));

        Assert.Equal(FailureKind.Auth, exception.Failure.Kind);
        Assert.Contains("login --tenant", exception.Failure.Hint, StringComparison.Ordinal);
        Assert.Empty(store.List());
    }

    [Fact]
    public void SaveThenResolveRoundTripsAndTheFirstProfileBecomesTheDefault()
    {
        string home = TempHome();
        var store = new ProfileStore(home);

        store.Save(Work);
        store.Save(Work with { Name = "lab", Tenant = "fabrikam.onmicrosoft.com" });

        Assert.Equal("work", store.DefaultName);
        Assert.Equal(Work, store.Resolve(null));
        Assert.Equal("fabrikam.onmicrosoft.com", store.Resolve("lab").Tenant);
        Assert.Equal(2, store.List().Count);
        string text = File.ReadAllText(Path.Combine(home, "config.json"));
        Assert.Contains("\"default\": \"work\"", text, StringComparison.Ordinal);
        Assert.Contains("\"clientId\"", text, StringComparison.Ordinal);
    }

    [Fact]
    public void SavingAgainReplacesTheProfileAndCanMoveTheDefault()
    {
        var store = new ProfileStore(TempHome());
        store.Save(Work);
        store.Save(Work with { Name = "lab" });

        store.Save(Work with { Name = "lab", Account = "bob@contoso.com" }, makeDefault: true);

        Assert.Equal("lab", store.DefaultName);
        Assert.Equal("bob@contoso.com", store.Resolve(null).Account);
    }

    [Fact]
    public void AnUnknownNameIsAnAuthFailure()
    {
        var store = new ProfileStore(TempHome());
        store.Save(Work);

        AegisException exception = Assert.Throws<AegisException>(() => store.Resolve("nope"));

        Assert.Contains("'nope'", exception.Failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AHandWrittenFileWithOneProfileNeedsNoDefaultKey()
    {
        string home = TempHome();
        Directory.CreateDirectory(home);
        File.WriteAllText(Path.Combine(home, "config.json"), """
            {
              // the maintainer's note
              "profiles": {
                "Work": { "Tenant": "contoso.onmicrosoft.com", "ClientId": "00000000-0000-0000-0000-000000000010", "Account": "alice@contoso.com" },
              }
            }
            """);

        TenantProfile profile = new ProfileStore(home).Resolve(null);

        Assert.Equal("Work", profile.Name);
        Assert.Equal("alice@contoso.com", profile.Account);
    }

    [Fact]
    public void AMalformedFileNamesThePath()
    {
        string home = TempHome();
        Directory.CreateDirectory(home);
        File.WriteAllText(Path.Combine(home, "config.json"), "{ not json");

        AegisException exception = Assert.Throws<AegisException>(() => new ProfileStore(home).Resolve(null));

        Assert.Equal(FailureKind.Auth, exception.Failure.Kind);
        Assert.Contains("config.json", exception.Failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DescribeMarksTheDefaultAndKeepsTheFilesOrder()
    {
        var store = new ProfileStore(TempHome());
        store.Save(Work);
        store.Save(Work with { Name = "lab", Tenant = "fabrikam.onmicrosoft.com" });

        ProfileList list = store.Describe();

        Assert.Equal(store.Path, list.File);
        Assert.Equal("work", list.Default);
        Assert.Equal(["work", "lab"], list.Profiles.Select(profile => profile.Name));
        Assert.Equal([true, false], list.Profiles.Select(profile => profile.IsDefault));
    }

    [Fact]
    public void WithoutAFileTheListIsEmpty()
    {
        ProfileList list = new ProfileStore(TempHome()).Describe();

        Assert.Empty(list.Profiles);
        Assert.Null(list.Default);
    }

    [Fact]
    public void TheOnlyProfileIsTheDefaultEvenWithoutTheKey()
    {
        string home = TempHome();
        Directory.CreateDirectory(home);
        File.WriteAllText(Path.Combine(home, "config.json"), """
            {"profiles":{"work":{"tenant":"contoso.onmicrosoft.com","clientId":"00000000-0000-0000-0000-000000000010","account":"alice@contoso.com"}}}
            """);

        ProfileList list = new ProfileStore(home).Describe();

        Assert.Equal("work", list.Default);
        Assert.True(Assert.Single(list.Profiles).IsDefault);
    }

    [Fact]
    public void UseMovesTheDefault()
    {
        var store = new ProfileStore(TempHome());
        store.Save(Work);
        store.Save(Work with { Name = "lab" });

        ProfileList list = store.Use("lab");

        Assert.Equal("lab", list.Default);
        Assert.Equal("lab", store.DefaultName);
        Assert.Equal("lab", store.Resolve(null).Name);
    }

    [Fact]
    public void UsingOrRemovingAnUnknownProfileIsNotFoundAndChangesNothing()
    {
        string home = TempHome();
        var store = new ProfileStore(home);
        store.Save(Work);
        string before = File.ReadAllText(store.Path);

        AegisException use = Assert.Throws<AegisException>(() => store.Use("nope"));
        AegisException remove = Assert.Throws<AegisException>(() => store.Remove("nope"));

        Assert.Equal(FailureKind.NotFound, use.Failure.Kind);
        Assert.Equal(FailureKind.NotFound, remove.Failure.Kind);
        Assert.Contains("profile list", use.Failure.Hint, StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllText(store.Path));
    }

    [Fact]
    public void RemovingTheDefaultLeavesTheOnlyOtherOneAsTheDefault()
    {
        var store = new ProfileStore(TempHome());
        store.Save(Work);
        store.Save(Work with { Name = "lab" });

        string? defaultAfter = store.Remove("work");

        Assert.Equal("lab", defaultAfter);
        Assert.Equal("lab", store.DefaultName);
    }

    [Fact]
    public void RemovingTheDefaultAmongSeveralLeavesNoDefault()
    {
        var store = new ProfileStore(TempHome());
        store.Save(Work);
        store.Save(Work with { Name = "lab" });
        store.Save(Work with { Name = "customer" });

        string? defaultAfter = store.Remove("work");

        Assert.Null(defaultAfter);
        Assert.Null(store.DefaultName);
        Assert.Throws<AegisException>(() => store.Resolve(null));
    }

    [Fact]
    public void RemovingAnotherProfileKeepsTheDefault()
    {
        var store = new ProfileStore(TempHome());
        store.Save(Work);
        store.Save(Work with { Name = "lab" });
        store.Save(Work with { Name = "customer" });

        Assert.Equal("work", store.Remove("lab"));
        Assert.Equal(["work", "customer"], store.List().Select(profile => profile.Name));
    }

    [Fact]
    public void RemovingTheLastProfileLeavesAnEmptyFile()
    {
        var store = new ProfileStore(TempHome());
        store.Save(Work);

        Assert.Null(store.Remove("work"));
        Assert.Empty(store.List());
        Assert.DoesNotContain("default", File.ReadAllText(store.Path), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{"profiles":{"work":null}}""")]
    [InlineData("""{"profiles":{"work":{"tenant":"contoso.onmicrosoft.com"}}}""")]
    public void AProfileWithoutItsValuesIsTheFilesFaultNotACrash(string text)
    {
        string home = TempHome();
        Directory.CreateDirectory(home);
        File.WriteAllText(Path.Combine(home, "config.json"), text);

        AegisException exception = Assert.Throws<AegisException>(() => new ProfileStore(home).Describe());

        Assert.Equal(FailureKind.Auth, exception.Failure.Kind);
        Assert.Contains("'work'", exception.Failure.Message, StringComparison.Ordinal);
    }

    internal static string TempHome() =>
        Path.Combine(Path.GetTempPath(), "m365-aegis-tests", Guid.NewGuid().ToString("N"));
}
