using System.Net;
using System.Text;
using System.Text.Json;
using M365Aegis.Cli.Commands;
using M365Aegis.Cli.Output;
using M365Aegis.Core.Auth;
using M365Aegis.Core.Config;
using M365Aegis.Core.Diagnostics;
using M365Aegis.Core.Exchange;
using M365Aegis.Core.Exchange.Sidecar;
using M365Aegis.Core.Graph;
using M365Aegis.Core.Operations;
using M365Aegis.Core.Output;
using M365Aegis.Core.Snapshots;
using M365Aegis.Tests;
using Microsoft.Identity.Client;

namespace M365Aegis.Cli.Tests;

/// <summary>
/// Token hygiene (task 072, SECURITY.md "Sign-in and tokens"): the tokens of one run carry a unique marker, and after
/// a login, a whoami, the sign-in library's verbose log, a plan applied with <c>--yes</c>, a failed apply, a failed
/// request, an internal error and an Exchange Online connect (accepted, then refused with the token in the module's
/// message), the marker is in no file under the temporary home folder (<c>config.json</c>, <c>logs\</c>, <c>plans\</c>)
/// and in neither stream the command line writes (the envelope and the table on stdout, the error lines on stderr).
/// In process, through the product's own parts with the test's token source: <see cref="AuthService"/> over a broker
/// that hands out marked tokens, <see cref="HttpGraphTransport"/> over a handler that answers like Graph (and checks
/// the token really went out as the bearer), the sidecar script over the fake module, <see cref="CommandRunner"/> and
/// its renderer with the streams given, and the home's <see cref="LogSink"/>. The Graph token is JWT-shaped, as the
/// service's are; the Exchange token is opaque, so only a sink that never takes it passes.
/// </summary>
public sealed class TokenHygieneTests
{
    private const string Tenant = "contoso.onmicrosoft.com";
    private const string ClientId = "00000000-0000-0000-0000-000000000010";
    private const string Account = MarkedTokenBroker.Account;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AMarkedTokenReachesNoFileOfTheHomeAndNoStream()
    {
        string marker = MarkedTokenBroker.NewMarker();
        var broker = new MarkedTokenBroker(marker);
        string home = PlanSamples.TempHome();
        var auth = new AuthService(home, _ => broker);
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var graphHandler = new GraphLikeHandler();

        // Both renderings of every answer: the envelope of a pipe and the table and error lines of a terminal.
        async Task RenderAsync<T>(string backend, Func<Task<CommandResult<T>>> work, ITableView<T> view)
        {
            foreach (OutputMode mode in new[] { OutputMode.Json, OutputMode.Table })
            {
                await CommandRunner.RunAsync(mode, backend, work, view, stdout, stderr, exception => InternalError.WriteToLog(exception, home));
                await stdout.WriteLineAsync();
            }
        }

        // login and whoami: the profile written to config.json, the tokens' expiry and scopes shown.
        await RenderAsync(Backend.Local, async () => CommandResult.Success(await auth.LoginAsync(new LoginOptions(null, Tenant, ClientId, Account, DeviceCode: false), _ => Task.CompletedTask, Ct)), SignInTableView.Instance);
        await RenderAsync(Backend.Local, async () => CommandResult.Success(await auth.WhoAmIAsync(null, Ct)), SignInTableView.Instance);

        // --verbose: the sign-in library's log into the home's logs folder, as MsalTokenBroker wires it, with the lines
        // the library writes around a token (a header, a query string, a JSON body, a bare JWT) and one it marks as PII.
        var log = LogSink.ForHome(home);
        LogCallback msal = MsalTokenBroker.ToSink(log);
        string token = broker.Token(Scopes.For(Resource.Graph));
        msal(LogLevel.Verbose, $"[HttpManager] request headers: Authorization: Bearer {token}", false);
        msal(LogLevel.Verbose, $"[Broker] redirect https://login.microsoftonline.com/common/oauth2/nativeclient?code={token}&state=1", false);
        msal(LogLevel.Info, $"[TokenResponse] {{\"token_type\":\"Bearer\",\"access_token\":\"{token}\",\"refresh_token\":\"{token}\"}}", false);
        msal(LogLevel.Verbose, $"[Cache] saved {token}", false);
        msal(LogLevel.Verbose, $"[Cache] the token {broker.Token(Scopes.For(Resource.Exchange))} for {Account}", true);

        // A plan applied with --yes, an apply the service refuses, and a request that never reaches it, over the real
        // HTTP transport with the profile's token.
        var graph = new GraphClient(
            new HttpGraphTransport(cancellationToken => auth.GetAccessTokenAsync(Resource.Graph, null, cancellationToken), graphHandler),
            options: new GraphClientOptions { Delay = (_, _) => Task.CompletedTask, Jitter = () => 0 });
        foreach (string group in new[] { "00000000-0000-0000-0000-000000000031", "00000000-0000-0000-0000-000000000032" })
        {
            string[] arguments = ["call", "PATCH", $"/groups/{group}", "--body", "{\"description\":\"made up\"}", "--yes"];
            var store = new PlanStore(home, new PlanRecordContext("command line", null, arguments));
            var options = new GraphCallOptions("PATCH", $"/groups/{group}", Beta: false, Body: "{\"description\":\"made up\"}", Yes: true, All: false);
            await RenderAsync(Backend.Graph, () => GraphCall.RunAsync(graph, options, store, Ct), CallTableView.Instance);
        }

        await RenderAsync(Backend.Graph, () => GraphCall.RunAsync(graph, new GraphCallOptions("GET", "/me", false, null, false, false), Ct), CallTableView.Instance);

        // An internal error whose message carries the header a library put into it: the envelope and the log's stack trace.
        await RenderAsync<object>(Backend.Graph, () => throw new InvalidOperationException($"the handler failed on Authorization: Bearer {token}"), NoTableView<object>.Instance);

        // Exchange Online: the sidecar connects with the profile's token and lists the mailboxes; then a connect the
        // fake module refuses with the token in its message.
        ExchangeTokenSource tokens = ExchangeTokenSource.ForProfile(auth, null);
        foreach (string organization in new[] { SidecarFixtures.Organization, "refuse.contoso.onmicrosoft.com" })
        {
            await using var session = new ExchangeSession(new ExchangeSessionOptions
            {
                Start = cancellationToken => SidecarClient.StartAsync(SidecarFixtures.StartInfo(), cancellationToken),
                Token = tokens.GetAsync,
                Target = _ => Task.FromResult(new ExchangeTarget(organization, null)),
            });
            await RenderAsync(Backend.Pwsh, () => MailboxLookups.ListAsync(session, sharedOnly: false, Ct), MailboxListTableView.Instance);
        }

        // A snapshot take (task 087) with the profile's tokens: Graph over the real HTTP transport (the users answer, every
        // other read fails and its message goes into the manifest), the sidecar over the fake module; then a take whose
        // connect the module refuses with the token in its message, which stops the take. The snapshots folder is
        // searched with the rest of the home.
        var snapshotHandler = new GraphLikeHandler { AnswerUsers = true };
        int takes = 0;
        foreach (string organization in new[] { SidecarFixtures.Organization, "refuse.contoso.onmicrosoft.com" })
        {
            await RenderAsync(Backend.Graph, async () =>
            {
                await using var session = new ExchangeSession(new ExchangeSessionOptions
                {
                    Start = cancellationToken => SidecarClient.StartAsync(SidecarFixtures.StartInfo(), cancellationToken),
                    Token = tokens.GetAsync,
                    Target = _ => Task.FromResult(new ExchangeTarget(organization, null)),
                });
                var snapshotGraph = new GraphClient(
                    new HttpGraphTransport(cancellationToken => auth.GetAccessTokenAsync(Resource.Graph, null, cancellationToken), snapshotHandler),
                    options: new GraphClientOptions { Delay = (_, _) => Task.CompletedTask, Jitter = () => 0 });
                await using var backends = new ExchangeBackends(session, null, () => snapshotGraph);
                var options = new SnapshotTakeOptions(home, new ProfileStore(home).Resolve(null)) { Clock = new SnapshotSamples.FixedClock(SnapshotSamples.Taken.AddMinutes(takes++)) };
                return await SnapshotTake.RunAsync(backends, options, Ct);
            }, SnapshotTakeTableView.Instance);
        }

        // The marker in no file of the home and in neither stream; a leak is named by its sink.
        string output = stdout.ToString();
        string errors = stderr.ToString();
        var sinks = new Dictionary<string, string>(StringComparer.Ordinal) { ["stdout"] = output, ["stderr"] = errors };
        foreach (string file in Directory.GetFiles(home, "*", SearchOption.AllDirectories))
        {
            sinks[Path.GetRelativePath(home, file)] = Encoding.UTF8.GetString(await File.ReadAllBytesAsync(file, Ct));
        }

        Assert.Empty(sinks.Where(sink => sink.Value.Contains(marker, StringComparison.Ordinal)).Select(sink => sink.Key));

        // The run did what it says: the profile and the records exist, the token went out as the bearer of every
        // Graph request, the first connect listed mailboxes and the second was refused.
        Assert.True(File.Exists(Path.Combine(home, "config.json")));
        Assert.Equal(4, Directory.GetFiles(Path.Combine(home, "plans")).Length);
        Assert.NotEmpty(Directory.GetFiles(Path.Combine(home, "logs")));
        // The applied PATCH, the refused one and the GET that fails, in both renderings.
        Assert.Equal(6, graphHandler.Bearers.Count);
        Assert.All(graphHandler.Bearers, bearer => Assert.Equal(token, bearer));
        Assert.Contains("finance@contoso.com", output, StringComparison.Ordinal);
        Assert.Contains("(forbidden)", errors, StringComparison.Ordinal);
        Assert.Contains("(service-error)", errors, StringComparison.Ordinal);
        Assert.Contains("(internal)", errors, StringComparison.Ordinal);
        // Two snapshots of the first take's two renderings, the Graph failures and the mailboxes in them; the second take
        // stopped at the refused connect and wrote none.
        Assert.Equal(2, Directory.GetFiles(Path.Combine(home, "snapshots", "default")).Length);
        Assert.All(snapshotHandler.Bearers, bearer => Assert.Equal(token, bearer));
        Assert.Contains("(partial)", errors, StringComparison.Ordinal);
        // The fake module put the token into its refusal; the sidecar's Hide-Secret took it out.
        Assert.Contains("the service refused the token <token> (401) (auth)", errors, StringComparison.Ordinal);
    }

    /// <summary>
    /// Answers like Graph and keeps every bearer it was sent: a PATCH of Finance (…031) applied (204), of Finance EU
    /// (…032) refused (403), and any other request a network failure.
    /// </summary>
    private sealed class GraphLikeHandler : HttpMessageHandler
    {
        internal List<string> Bearers { get; } = [];

        /// <summary>Whether a GET of the users answers (the snapshot's first read) with one made-up user.</summary>
        internal bool AnswerUsers { get; init; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            lock (Bearers)
            {
                Bearers.Add(request.Headers.Authorization?.Parameter ?? "");
            }

            string path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Patch && path.EndsWith("00000000-0000-0000-0000-000000000031", StringComparison.Ordinal))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
            }

            if (request.Method == HttpMethod.Patch)
            {
                var refused = new HttpResponseMessage(HttpStatusCode.Forbidden)
                {
                    Content = new StringContent(JsonSerializer.Serialize(new { error = new { code = "Authorization_RequestDenied", message = "Insufficient privileges to complete the operation." } }), Encoding.UTF8, "application/json"),
                };
                refused.Headers.Add("request-id", "00000000-0000-0000-0000-0000000000e1");
                return Task.FromResult(refused);
            }

            if (AnswerUsers && request.Method == HttpMethod.Get && path == "/v1.0/users")
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(new { value = new[] { new { id = "00000000-0000-0000-0000-000000000001", displayName = "Alice Example", userPrincipalName = Account } } }), Encoding.UTF8, "application/json"),
                });
            }

            throw new HttpRequestException("No such host is known. (graph.microsoft.com:443)");
        }
    }
}
