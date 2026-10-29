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

    internal static string TempHome() =>
        Path.Combine(Path.GetTempPath(), "m365-aegis-tests", Guid.NewGuid().ToString("N"));
}
