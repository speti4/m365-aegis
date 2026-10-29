using M365Aegis.Core.Auth;
using M365Aegis.Core.Output;

namespace M365Aegis.Core.Graph;

/// <summary>
/// Makes the Graph client a command uses: over the network with the profile's Graph token from
/// <see cref="AuthService.GetAccessTokenAsync"/> (silent only, so a lookup never prompts; no sign-in is an exit
/// code 3 with the login hint), or over the recorded fixtures when <see cref="FixturesEnvironmentVariable"/> names
/// a folder, which is how the command line's own tests run every lookup offline. Like
/// <c>M365AEGIS_EXO_MODULE_ROOT</c> for the sidecar, the variable only ever makes the tool read local files.
/// </summary>
public static class GraphConnection
{
    /// <summary>The environment variable that points the client at a fixture folder instead of the network.</summary>
    public const string FixturesEnvironmentVariable = "M365AEGIS_GRAPH_FIXTURES";

    /// <summary>The client of a command: the real home folder's sign-in, or the fixtures when the variable is set.</summary>
    /// <param name="profileName">The tenant profile (<c>--profile</c>), or null for the default one.</param>
    /// <param name="requestLog">Where the request ids go (the command runner's log).</param>
    public static GraphClient Create(string? profileName, RequestLog requestLog) => Create(profileName, requestLog, AuthService.Create);

    /// <summary>
    /// The client of a caller that holds its own sign-in (the window, whose broker takes the window as the picker's
    /// parent): the fixtures when the variable is set, else the network with the sign-in <paramref name="auth"/> makes.
    /// </summary>
    /// <param name="profileName">The tenant profile, or null for the default one.</param>
    /// <param name="requestLog">Where the request ids go.</param>
    /// <param name="auth">The sign-in; not called when the fixtures answer.</param>
    public static GraphClient Create(string? profileName, RequestLog requestLog, Func<AuthService> auth)
    {
        ArgumentNullException.ThrowIfNull(auth);
        string? fixtures = Environment.GetEnvironmentVariable(FixturesEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(fixtures))
        {
            return new GraphClient(new FixtureGraphTransport(Path.GetFullPath(fixtures)), requestLog);
        }

        return Create(auth(), profileName, requestLog);
    }

    /// <summary>The client over the network with the given sign-in; the token is asked for on the first request.</summary>
    public static GraphClient Create(AuthService auth, string? profileName, RequestLog requestLog, GraphClientOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(auth);
        var transport = new HttpGraphTransport(cancellationToken => auth.GetAccessTokenAsync(Resource.Graph, profileName, cancellationToken));
        return new GraphClient(transport, requestLog, options);
    }
}
