using System.Text;
using System.Text.Json.Nodes;
using M365Aegis.Core.Auth;
using M365Aegis.Core.Bridge;
using M365Aegis.Core.Config;
using M365Aegis.Core.Diagnostics;
using M365Aegis.Core.Exchange;
using M365Aegis.Core.Exchange.AdminApi;
using M365Aegis.Core.Exchange.Sidecar;
using M365Aegis.Core.Graph;
using M365Aegis.Core.Tests.Config;
using M365Aegis.Tests;

namespace M365Aegis.Core.Tests;

/// <summary>
/// Token hygiene through the window (task 080, after task 072's run of the command line): the tokens of one run
/// carry a unique marker, and after the bridge's sign-in requests (<c>whoami</c>, <c>login</c> with the account picker
/// and with a device code), a group lookup whose Graph request fails with the bearer header in its message (the
/// <c>internal</c> error reply and its stack trace in the log), the mailbox lookups over the window's Exchange Online
/// token source (the list from the Admin API, a user's access split across three sidecars, a mailbox's over both
/// layers), a connect the fake module refuses with the token in its message (an <c>error</c> reply) and a
/// <c>logout</c>, all under <c>--verbose</c> so every mailbox request writes its <c>window:</c> line, the marker is in
/// no file under the temporary home folder (<c>config.json</c>, <c>logs\</c>), in no reply or notification as the host
/// sends it to the page, and not on the window's stderr. In process, through the product's own parts with the test's
/// token source: <see cref="BridgeDispatcher"/> over <see cref="AuthService"/> with the window's hints and the broker
/// of <see cref="MarkedTokenBroker"/>, <see cref="HttpGraphTransport"/> with the profile's Graph token,
/// <see cref="WindowExchange"/> over <see cref="ExchangeTokenSource.ForProfile"/> with its sessions on the fake module
/// and the Admin API over the recorded answers, and the home's <see cref="LogSink"/> as the window's verbose sink.
/// </summary>
public sealed class BridgeTokenHygieneTests
{
    private const string Refused = "refuse.contoso.onmicrosoft.com";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AMarkedTokenReachesNoFileOfTheHomeNoMessageToThePageAndNoStderr()
    {
        string marker = MarkedTokenBroker.NewMarker();
        // The tokens name the tenant of the Admin API's recorded answers, so the window's mailbox lookups read them.
        var broker = new MarkedTokenBroker(marker, AdminApiConnection.TestTenantId);
        string home = ProfileStoreTests.TempHome();
        // The profile the command line's `login --tenant … --client-id … --account …` writes; the window only reads it.
        new ProfileStore(home).Save(new TenantProfile("work", "contoso.onmicrosoft.com", "00000000-0000-0000-0000-000000000010", MarkedTokenBroker.Account));
        var auth = new AuthService(home, _ => broker, SignInHints.Window);
        var graphHandler = new FailingGraphHandler();
        var graph = new GraphClient(
            new HttpGraphTransport(cancellationToken => auth.GetAccessTokenAsync(Resource.Graph, null, cancellationToken), graphHandler),
            options: new GraphClientOptions { Delay = (_, _) => Task.CompletedTask, Jitter = () => 0 });

        // The window's layers as BridgeServices.ForWindow makes them, over the profile's Exchange Online token; and a
        // window whose sidecar connects to an organization the fake module refuses, naming the token in its message.
        await using var exchange = new WindowExchange(
            () => ExchangeTokenSource.ForProfile(auth, null),
            tokens => SidecarFixtures.Session(tokens.GetAsync),
            tokens => AdminApiConnection.Create(tokens, new FixtureGraphTransport(GraphFixtures.Root), null));
        await using var refusing = new WindowExchange(
            () => ExchangeTokenSource.ForProfile(auth, null),
            tokens => SidecarFixtures.Session(tokens.GetAsync, new ExchangeTarget(Refused, null)),
            _ => null);
        var services = new BridgeServices(auth, null, _ => graph, exception => InternalError.WriteToLog(exception, home)) { Exchange = exchange };
        var window = new BridgeDispatcher(services);
        var refusingWindow = new BridgeDispatcher(services with { Exchange = refusing });

        // Every message the host would send to the page, as it writes it: the replies and the notifications.
        var sent = new List<string>();
        var replies = new List<BridgeMessage>();
        async Task<BridgeMessage> SendAsync(BridgeDispatcher dispatcher, string type, string? data = null)
        {
            var request = new BridgeMessage(BridgeProtocol.Version, $"{replies.Count + 1}", type, data is null ? null : JsonNode.Parse(data));
            BridgeMessage reply = (await dispatcher.DispatchAsync(
                request,
                notification =>
                {
                    sent.Add(BridgeProtocol.Write(notification));
                    return Task.CompletedTask;
                },
                Ct))!;
            sent.Add(BridgeProtocol.Write(reply));
            replies.Add(reply);
            return reply;
        }

        TextWriter stderrBefore = Console.Error;
        var stderr = new StringWriter();
        Console.SetError(stderr);
        string exchangeToken;
        try
        {
            using (LogSink.UseVerbose(LogSink.ForHome(home)))
            {
                await SendAsync(window, BridgeMessageType.WhoAmI);
                await SendAsync(window, BridgeMessageType.Login);
                await SendAsync(window, BridgeMessageType.Login, """{"deviceCode":true}""");
                await SendAsync(window, BridgeMessageType.GroupMembers, """{"group":"Finance"}""");
                await SendAsync(window, BridgeMessageType.MailboxList);
                await SendAsync(window, BridgeMessageType.MailboxAccess, """{"user":"bob@contoso.com"}""");
                await SendAsync(window, BridgeMessageType.MailboxAccess, """{"mailbox":"finance@contoso.com"}""");
                await SendAsync(refusingWindow, BridgeMessageType.MailboxList);
                exchangeToken = exchange.Session!.Token!.AccessToken;
                await SendAsync(window, BridgeMessageType.Logout);
            }
        }
        finally
        {
            Console.SetError(stderrBefore);
        }

        // The marker in no file of the home, no message to the page and not on stderr; a leak is named by its sink.
        var sinks = new Dictionary<string, string>(StringComparer.Ordinal) { ["stderr"] = stderr.ToString() };
        for (int index = 0; index < sent.Count; index++)
        {
            sinks[$"message {index + 1} to the page"] = sent[index];
        }

        foreach (string file in Directory.GetFiles(home, "*", SearchOption.AllDirectories))
        {
            sinks[Path.GetRelativePath(home, file)] = Encoding.UTF8.GetString(await File.ReadAllBytesAsync(file, Ct));
        }

        Assert.Empty(sinks.Where(sink => sink.Value.Contains(marker, StringComparison.Ordinal)).Select(sink => sink.Key));

        // The run did what it says: each request got its answer, the device code reached the page, the Graph token
        // went out as the bearer, the window's sidecars connected with the profile's Exchange Online token, and every
        // mailbox request wrote its `window:` line.
        Assert.Equal(
            ["signed-in", "signed-in", "signed-in", "error", "mailboxes", "user-grants", "mailbox-holders", "error", "logged-out"],
            replies.Select(reply => reply.Type));
        Assert.Contains(sent, message => message.Contains("\"type\":\"device-code\"", StringComparison.Ordinal));
        Assert.Equal("internal", replies[3].Data?["code"]?.GetValue<string>());
        Assert.Equal("auth", replies[7].Data?["code"]?.GetValue<string>());
        // The fake module put the token into its refusal; the sidecar's Hide-Secret took it out.
        Assert.Contains("the service refused the token <token> (401)", replies[7].Data?["message"]?.GetValue<string>(), StringComparison.Ordinal);
        Assert.NotEmpty(graphHandler.Bearers);
        Assert.All(graphHandler.Bearers, bearer => Assert.Equal(broker.Token(Scopes.For(Resource.Graph)), bearer));
        Assert.Equal(broker.Token(Scopes.For(Resource.Exchange)), exchangeToken);
        string log = string.Join("\n", Directory.GetFiles(Path.Combine(home, LogSink.FolderName)).Select(File.ReadAllText));
        Assert.Equal(4, log.Split('\n').Count(line => line.Contains("window: mailbox-", StringComparison.Ordinal)));
        Assert.Contains("over 3 sidecar(s)", log, StringComparison.Ordinal);
        Assert.Contains("internal-error: ", log, StringComparison.Ordinal);
        Assert.Equal(string.Empty, stderr.ToString());
    }

    /// <summary>A handler that keeps every bearer it was sent and fails with it in the message, as a broken library might.</summary>
    private sealed class FailingGraphHandler : HttpMessageHandler
    {
        internal List<string> Bearers { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string bearer = request.Headers.Authorization?.Parameter ?? "";
            lock (Bearers)
            {
                Bearers.Add(bearer);
            }

            throw new InvalidOperationException($"the handler failed on Authorization: Bearer {bearer}");
        }
    }
}
