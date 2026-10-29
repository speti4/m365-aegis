using System.Globalization;
using M365Aegis.Cli.Output;
using M365Aegis.Core.Auth;

namespace M365Aegis.Cli.Commands;

/// <summary>The table of <c>login</c> and <c>whoami</c>: a field per row, the account first, then a token's state per resource.</summary>
internal sealed class SignInTableView : ITableView<SignInInfo>
{
    /// <summary>The one instance.</summary>
    internal static SignInTableView Instance { get; } = new();

    private SignInTableView()
    {
    }

    public IReadOnlyList<string> Headers => ["field", "value"];

    public IEnumerable<IReadOnlyList<string>> Rows(SignInInfo data)
    {
        yield return ["profile", data.Profile];
        yield return ["account", data.Account];
        if (data.DisplayName is not null)
        {
            yield return ["name", data.DisplayName];
        }

        yield return ["tenant", data.TenantId ?? "-"];
        yield return ["object id", data.ObjectId ?? "-"];
        foreach (ResourceToken token in data.Tokens)
        {
            string resource = Name(token.Resource);
            if (token.ExpiresOn is { } expiresOn)
            {
                string origin = token.Origin switch
                {
                    TokenOrigin.Broker => "the Windows account manager",
                    TokenOrigin.Cache => "the cache",
                    _ => "the identity provider",
                };
                yield return [$"{resource} token", string.Format(CultureInfo.InvariantCulture, "expires {0:yyyy-MM-dd HH:mm zzz}, from {1}", expiresOn, origin)];
                yield return [$"{resource} scopes", token.Scopes.Count == 0 ? "-" : string.Join(" ", token.Scopes)];
            }
            else
            {
                yield return [$"{resource} token", $"none ({token.Error ?? "no reason given"})"];
            }
        }
    }

    /// <summary>The resource's name in the table.</summary>
    internal static string Name(Resource resource) => resource switch
    {
        Resource.Graph => "graph",
        Resource.Exchange => "exchange",
        _ => "?",
    };
}

/// <summary>The table of <c>logout</c>.</summary>
internal sealed class SignOutTableView : ITableView<SignOutInfo>
{
    /// <summary>The one instance.</summary>
    internal static SignOutTableView Instance { get; } = new();

    private SignOutTableView()
    {
    }

    public IReadOnlyList<string> Headers => ["field", "value"];

    public IEnumerable<IReadOnlyList<string>> Rows(SignOutInfo data)
    {
        yield return ["profile", data.Profile];
        yield return ["account", data.Account];
        yield return ["removed accounts", data.RemovedAccounts.ToString(CultureInfo.InvariantCulture)];
    }

    public IEnumerable<string> Footer(SignOutInfo data)
    {
        yield return data.RemovedAccounts == 0 ? "nothing was signed in; the profile stays" : "signed out; the profile stays";
    }
}
