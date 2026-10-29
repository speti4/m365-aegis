using System.Text.Json;
using M365Aegis.Core.Auth;
using M365Aegis.Core.Config;
using M365Aegis.Core.Output;

namespace M365Aegis.Tests;

/// <summary>
/// The scrubbed sign-in results under tests/fixtures/auth, read back with the contract's own JSON options, so the
/// snapshot tests of the Core and the Cli render the same samples (as <see cref="DoctorSamples"/> does for the
/// doctor). Linked into both test projects.
/// </summary>
internal static class AuthSamples
{
    /// <summary>Both tokens from the Windows account manager.</summary>
    internal static SignInInfo SignedIn => Read("signed-in.json");

    /// <summary>A Graph token from the cache and no Exchange Online token: the consent is missing.</summary>
    internal static SignInInfo NoExchangeToken => Read("no-exchange-token.json");

    /// <summary>One account record removed.</summary>
    internal static SignOutInfo SignedOut { get; } = new("default", "alice@contoso.com", 1);

    /// <summary>Two profiles of <c>profile list</c>, the first the default (task 024).</summary>
    internal static ProfileList Profiles { get; } = new(
        @"D:\aegis-home\config.json",
        "work",
        [
            new("work", "contoso.onmicrosoft.com", "00000000-0000-0000-0000-000000000010", "alice@contoso.com", IsDefault: true),
            new("lab", "fabrikam.onmicrosoft.com", "00000000-0000-0000-0000-000000000011", "alice@fabrikam.com", IsDefault: false),
        ]);

    /// <summary>Two profiles and no default: the footer says how to pick one.</summary>
    internal static ProfileList ProfilesWithoutDefault { get; } = Profiles with
    {
        Default = null,
        Profiles = [.. Profiles.Profiles.Select(profile => profile with { IsDefault = false })],
    };

    /// <summary>A profile removed after its account was signed out; the other one is the default.</summary>
    internal static ProfileRemoval RemovedProfile { get; } = new("lab", "alice@fabrikam.com", SignedOut: true, RemovedAccounts: 1, Default: "work");

    private static SignInInfo Read(string fileName)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "fixtures", "auth", fileName);
        return JsonSerializer.Deserialize<SignInInfo>(File.ReadAllText(path), EnvelopeJson.Options)
            ?? throw new InvalidOperationException($"{path} holds no sign-in result");
    }
}
