using System.Text.Json;
using System.Text.Json.Nodes;
using M365Aegis.Core.Auth;
using M365Aegis.Core.Bridge;
using M365Aegis.Core.Config;
using M365Aegis.Core.Diagnostics;
using M365Aegis.Core.Exchange;
using M365Aegis.Core.Exchange.AdminApi;
using M365Aegis.Core.Exchange.Sidecar;
using M365Aegis.Core.Graph;
using M365Aegis.Core.Output;
using M365Aegis.Core.Tests.Auth;
using M365Aegis.Core.Tests.Config;
using M365Aegis.Tests;

namespace M365Aegis.Core.Tests;

/// <summary>
/// The bridge between the window's front end and the core (tasks 017, 030, 040, 045, 056, 057 and 063): the protocol, and the
/// dispatcher's answers held against the samples under tests/contracts/bridge, which the front end's Vitest tests
/// read too. A request sample dispatched over a scripted sign-in, the recorded Graph answers and the window's
/// Exchange Online session over the fake module gives exactly its reply sample.
/// </summary>
public sealed class BridgeProtocolTests
{
    private static readonly string Contracts = Path.Combine(AppContext.BaseDirectory, "contracts", "bridge");
    private static readonly CancellationToken None = CancellationToken.None;

    [Fact]
    public async Task PingIsAnsweredWithPongAndTheVersion()
    {
        BridgeMessage request = Sample("ping.json");

        BridgeMessage? reply = await Dispatcher().DispatchAsync(request, None);

        Assert.NotNull(reply);
        Assert.Equal("pong", reply.Type);
        Assert.Equal(request.Id, reply.Id);
        Assert.Equal(ProductInfo.Version, reply.Data?["version"]?.GetValue<string>());
        Assert.Equal(ProductInfo.Name, reply.Data?["product"]?.GetValue<string>());
        // The pong sample has the same shape; its version is the sample's own.
        Assert.Equal(["product", "version"], Sample("pong.json").Data!.AsObject().Select(property => property.Key));
    }

    [Fact]
    public async Task ReadyIsANotificationWithNoAnswer()
    {
        BridgeMessage ready = Sample("ready.json");
        Assert.Null(ready.Id);

        Assert.Null(await Dispatcher().DispatchAsync(ready, None));
    }

    [Fact]
    public async Task WhoAmIOfASignedInAccountIsTheSignedInSample()
    {
        var broker = new FakeTokenBroker
        {
            OperatingSystemAccount = scope => FakeTokenBroker.Token(scope, FakeTokenBroker.Alice, TokenOrigin.Broker),
            Silent = (scope, account) => FakeTokenBroker.Token(scope, account, TokenOrigin.Broker),
        };

        BridgeMessage? reply = await Dispatcher(broker).DispatchAsync(Sample("whoami.json"), None);

        AssertIsSample("signed-in.json", reply);
    }

    [Fact]
    public async Task WhoAmIWithoutASilentSignInIsTheSignedOutSampleWithTheProfile()
    {
        var broker = new FakeTokenBroker();
        broker.Accounts.Add(FakeTokenBroker.Bob);

        BridgeMessage? reply = await Dispatcher(broker).DispatchAsync(Sample("whoami.json"), None);

        AssertIsSample("signed-out.json", reply);
        Assert.DoesNotContain(broker.Calls, call => call.StartsWith("interactive", StringComparison.Ordinal));
    }

    [Fact]
    public async Task WhoAmIWithoutAProfileIsSignedOutWithTheWindowsHint()
    {
        string home = ProfileStoreTests.TempHome();
        var broker = new FakeTokenBroker();

        BridgeMessage? reply = await Dispatcher(broker, home).DispatchAsync(Sample("whoami.json"), None);

        Assert.NotNull(reply);
        Assert.Equal("signed-out", reply.Type);
        Assert.Null(reply.Data?["profile"]);
        Assert.Equal(SignInHints.Window.NoProfile, reply.Data?["hint"]?.GetValue<string>());
        Assert.Empty(broker.Calls);
    }

    [Fact]
    public async Task GroupMembersIsTheMembersSample()
    {
        BridgeMessage? reply = await Dispatcher().DispatchAsync(Sample("group-members.json"), None);

        AssertIsSample("members.json", reply);
    }

    [Fact]
    public async Task AGroupThatIsNotThereIsTheErrorSample()
    {
        BridgeMessage request = Sample("group-members.json") with { Id = "4", Data = new JsonObject { ["group"] = "Nothing" } };

        BridgeMessage? reply = await Dispatcher().DispatchAsync(request, None);

        AssertIsSample("error.json", reply);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("""{}""")]
    [InlineData("""{"group":"  "}""")]
    [InlineData("""{"group":42}""")]
    [InlineData("""["Finance"]""")]
    public async Task AGroupMembersRequestWithoutAGroupIsAUsageError(string? data)
    {
        var request = new BridgeMessage(BridgeProtocol.Version, "5", "group-members", data is null ? null : JsonNode.Parse(data));

        BridgeMessage? reply = await Dispatcher().DispatchAsync(request, None);

        Assert.NotNull(reply);
        Assert.Equal("error", reply.Type);
        Assert.Equal("5", reply.Id);
        Assert.Equal("usage", reply.Data?["code"]?.GetValue<string>());
    }

    [Fact]
    public async Task AnUnknownTypeIsAnError()
    {
        var request = new BridgeMessage(BridgeProtocol.Version, "7", "frobnicate", null);

        BridgeMessage? reply = await Dispatcher().DispatchAsync(request, None);

        Assert.NotNull(reply);
        Assert.Equal("error", reply.Type);
        Assert.Equal("unknown-type", reply.Data?["code"]?.GetValue<string>());
    }

    [Fact]
    public async Task AnUnexpectedExceptionIsTheInternalErrorWithTheStackTraceInTheLog()
    {
        string home = WithProfile();
        var services = new BridgeServices(
            new AuthService(home, _ => new FakeTokenBroker()),
            null,
            _ => throw new InvalidOperationException("the client broke for alice@contoso.com"),
            exception => InternalError.WriteToLog(exception, home));

        BridgeMessage? reply = await new BridgeDispatcher(services).DispatchAsync(Sample("group-members.json"), None);

        Assert.NotNull(reply);
        Assert.Equal("error", reply.Type);
        Assert.Equal("internal", reply.Data?["code"]?.GetValue<string>());
        string message = reply.Data!["message"]!.GetValue<string>();
        Assert.Contains("InvalidOperationException", message, StringComparison.Ordinal);
        Assert.DoesNotContain("alice@contoso.com", message, StringComparison.Ordinal);
        string log = Assert.Single(Directory.GetFiles(Path.Combine(home, LogSink.FolderName)));
        Assert.Contains(log, reply.Data["hint"]!.GetValue<string>(), StringComparison.Ordinal);
        string text = File.ReadAllText(log);
        Assert.Contains("internal-error: System.InvalidOperationException", text, StringComparison.Ordinal);
        Assert.DoesNotContain("alice@contoso.com", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoginThroughTheAccountPickerIsTheSignedInReply()
    {
        var broker = new FakeTokenBroker
        {
            Interactive = (scope, _) => FakeTokenBroker.Token(scope, FakeTokenBroker.Alice, TokenOrigin.Broker),
            Silent = (scope, account) => FakeTokenBroker.Token(scope, account, TokenOrigin.Broker),
        };
        var notifications = new List<BridgeMessage>();

        BridgeMessage? reply = await Dispatcher(broker).DispatchAsync(Sample("login.json"), Collect(notifications), None);

        AssertIsSample("signed-in.json", reply, id: "5");
        Assert.Equal(["os-account", "accounts", "interactive:alice@contoso.com", "silent:alice@contoso.com"], broker.Calls);
        Assert.Empty(notifications);
    }

    [Fact]
    public async Task LoginWithADeviceCodeSendsTheCodeAsANotificationBeforeTheReply()
    {
        var broker = new FakeTokenBroker
        {
            DeviceCode = scope => FakeTokenBroker.Token(scope, FakeTokenBroker.Alice, TokenOrigin.IdentityProvider),
            Silent = (scope, account) => FakeTokenBroker.Token(scope, account, TokenOrigin.Cache),
        };
        var notifications = new List<BridgeMessage>();
        BridgeMessage request = Sample("login.json") with { Data = new JsonObject { ["deviceCode"] = true } };

        BridgeMessage? reply = await Dispatcher(broker).DispatchAsync(request, Collect(notifications), None);

        Assert.Equal("signed-in", reply?.Type);
        Assert.DoesNotContain(broker.Calls, call => call.StartsWith("interactive", StringComparison.Ordinal));
        BridgeMessage notification = Assert.Single(notifications);
        AssertHasTheShapeOf("device-code.json", notification);
        Assert.Equal("ABCD1234", notification.Data?["userCode"]?.GetValue<string>());
    }

    [Fact]
    public async Task LoginFallsBackToADeviceCodeWhenThePickerHasNoWindow()
    {
        var broker = new FakeTokenBroker
        {
            InteractiveFailure = new BrokerException(BrokerErrorKind.WindowHandleRequired, MsalTokenBroker.WindowHandleRequiredCode, "no window"),
            DeviceCode = scope => FakeTokenBroker.Token(scope, FakeTokenBroker.Alice, TokenOrigin.IdentityProvider),
            Silent = (scope, account) => FakeTokenBroker.Token(scope, account, TokenOrigin.Cache),
        };
        var notifications = new List<BridgeMessage>();

        BridgeMessage? reply = await Dispatcher(broker).DispatchAsync(Sample("login.json"), Collect(notifications), None);

        Assert.Equal("signed-in", reply?.Type);
        Assert.Contains("interactive:alice@contoso.com", broker.Calls);
        Assert.Contains("device-code", broker.Calls);
        Assert.Equal("device-code", Assert.Single(notifications).Type);
    }

    [Fact]
    public async Task ALoginCancelledInThePickerIsAnAuthError()
    {
        var broker = new FakeTokenBroker
        {
            InteractiveFailure = new BrokerException(BrokerErrorKind.Cancelled, "authentication_canceled", "the user closed the picker"),
        };

        BridgeMessage? reply = await Dispatcher(broker).DispatchAsync(Sample("login.json"), None);

        Assert.Equal("error", reply?.Type);
        Assert.Equal("5", reply?.Id);
        Assert.Equal("auth", reply?.Data?["code"]?.GetValue<string>());
        Assert.Contains("cancelled", reply?.Data?["message"]?.GetValue<string>(), StringComparison.Ordinal);
        Assert.Equal(SignInHints.Window.PickerCancelled, reply?.Data?["hint"]?.GetValue<string>());
    }

    [Fact]
    public async Task ALoginWithoutAProfileIsAnAuthErrorThatSendsToTheAdministrator()
    {
        var broker = new FakeTokenBroker();

        BridgeMessage? reply = await Dispatcher(broker, ProfileStoreTests.TempHome()).DispatchAsync(Sample("login.json"), None);

        Assert.Equal("error", reply?.Type);
        Assert.Equal("auth", reply?.Data?["code"]?.GetValue<string>());
        Assert.Equal(SignInHints.Window.NoProfile, reply?.Data?["hint"]?.GetValue<string>());
        Assert.Empty(broker.Calls);
    }

    [Fact]
    public async Task LogoutIsTheLoggedOutSampleAndEndsTheWindowsSidecar()
    {
        var broker = new FakeTokenBroker();
        broker.Accounts.Add(FakeTokenBroker.Alice);
        await using var exchange = new WindowExchange(() => SidecarFixtures.Session());
        BridgeDispatcher dispatcher = Dispatcher(broker, exchange: exchange);
        Assert.Equal("mailboxes", (await dispatcher.DispatchAsync(Sample("mailbox-list.json"), None))?.Type);
        SidecarClient sidecar = exchange.Session!.Client!;

        BridgeMessage? reply = await dispatcher.DispatchAsync(Sample("logout.json"), None);

        AssertIsSample("logged-out.json", reply);
        Assert.Equal([FakeTokenBroker.Alice], broker.Removed);
        Assert.Null(exchange.Session);
        Assert.True(sidecar.HasExited);
    }

    [Fact]
    public async Task MailboxListIsTheMailboxesSample()
    {
        await using var exchange = new WindowExchange(() => SidecarFixtures.Session());

        BridgeMessage? reply = await Dispatcher(exchange: exchange).DispatchAsync(Sample("mailbox-list.json"), None);

        AssertIsSample("mailboxes.json", reply);
    }

    [Fact]
    public async Task MailboxAccessOfAMailboxIsTheMailboxHoldersSampleOverBothLayers()
    {
        await using WindowExchange exchange = Layers();

        BridgeMessage? reply = await Dispatcher(exchange: exchange).DispatchAsync(Sample("mailbox-access.json"), None);

        // The mailbox and Bob by name from the Admin API, Alice's FullAccess and Send As from the session (task 063).
        AssertIsSample("mailbox-holders.json", reply);
        Assert.Equal(1, exchange.Session!.Starts);
        Assert.Equal(1, exchange.Tokens!.Acquisitions);
    }

    [Fact]
    public async Task AWindowWithoutAnAdminApiReadsTheAccessOnItsSessionAndNamesThatLayer()
    {
        await using WindowExchange exchange = Layers(adminApi: false);

        BridgeMessage? reply = await Dispatcher(exchange: exchange).DispatchAsync(Sample("mailbox-access.json"), None);

        AssertIs(ThroughTheSession(SampleNode("mailbox-holders.json")), reply);
        Assert.Equal(
            "no Admin API in this window: the mailbox, 1 Send on Behalf delegate(s) and 2 FullAccess and Send As grant(s) from pwsh",
            BridgeDispatcher.DescribeLayers(hasAdminApi: false, Holders(reply)));
    }

    [Fact]
    public async Task AWindowHolderMadeWithoutLayersReadsTheAccessOnItsSession()
    {
        await using var exchange = new WindowExchange(() => SidecarFixtures.Session());

        BridgeMessage? reply = await Dispatcher(exchange: exchange).DispatchAsync(Sample("mailbox-access.json"), None);

        AssertIs(ThroughTheSession(SampleNode("mailbox-holders.json")), reply);
    }

    [Fact]
    public async Task AnAdminApiThatCannotAnswerLeavesTheAccessToTheSession()
    {
        await using WindowExchange exchange = Layers();
        BridgeMessage request = Sample("mailbox-access.json") with { Data = new JsonObject { ["mailbox"] = "bob" } };

        // The recorded Admin API refuses bob's mailbox (403): the session reads it, as `mailbox access bob` does.
        BridgeMessage? reply = await Dispatcher(exchange: exchange).DispatchAsync(request, None);

        Assert.Equal("mailbox-holders", reply?.Type);
        MailboxAccess access = Holders(reply);
        Assert.Equal("Bob Example", access.Mailbox.DisplayName);
        Assert.Equal(Backend.Pwsh, access.MailboxLayer);
        Assert.Equal(
            "the Admin API could not answer: the mailbox, 0 Send on Behalf delegate(s) and 0 FullAccess and Send As grant(s) from pwsh",
            BridgeDispatcher.DescribeLayers(hasAdminApi: true, access));
    }

    [Fact]
    public async Task AMailboxTheAdminApiDoesNotFindIsANotFoundErrorWithTheWindowsHint()
    {
        await using WindowExchange exchange = Layers();
        BridgeMessage request = Sample("mailbox-access.json") with { Data = new JsonObject { ["mailbox"] = "nobody" } };

        BridgeMessage? reply = await Dispatcher(exchange: exchange).DispatchAsync(request, None);

        Assert.Equal("error", reply?.Type);
        Assert.Equal("not-found", reply?.Data?["code"]?.GetValue<string>());
        Assert.Equal("mailbox 'nobody' not found", reply?.Data?["message"]?.GetValue<string>());
        Assert.Equal(SignInHints.Window.Failures[FailureKind.NotFound], reply?.Data?["hint"]?.GetValue<string>());
    }

    [Fact]
    public void TheLogLineNamesWhatEachLayerReadByCountsOnly()
    {
        MailboxAccess access = Holders(Sample("mailbox-holders.json"));

        string layers = BridgeDispatcher.DescribeLayers(hasAdminApi: true, access);

        Assert.Equal("the mailbox and 1 Send on Behalf delegate(s) from admin-api, 2 FullAccess and Send As grant(s) from pwsh", layers);
        Assert.DoesNotContain("contoso", layers, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Bob", layers, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MailboxAccessOfAUserIsTheUserGrantsSample()
    {
        await using var exchange = new WindowExchange(() => SidecarFixtures.Session());
        var request = new BridgeMessage(BridgeProtocol.Version, "9", "mailbox-access", new JsonObject { ["user"] = "bob@contoso.com" });

        BridgeMessage? reply = await Dispatcher(exchange: exchange).DispatchAsync(request, None);

        AssertIsSample("user-grants.json", reply);
    }

    [Fact]
    public async Task TwoMailboxRequestsInARowRunOnOneSidecarWithOneConnect()
    {
        await using var exchange = new WindowExchange(() => SidecarFixtures.Session());
        BridgeDispatcher dispatcher = Dispatcher(exchange: exchange);
        Assert.Null(exchange.Session);

        BridgeMessage? first = await dispatcher.DispatchAsync(Sample("mailbox-list.json"), None);
        SidecarConnected? connection = exchange.Session?.Connection;
        BridgeMessage? second = await dispatcher.DispatchAsync(Sample("mailbox-access.json"), None);

        Assert.Equal("mailboxes", first?.Type);
        Assert.Equal("mailbox-holders", second?.Type);
        Assert.Equal(1, exchange.Sessions);
        Assert.Equal(1, exchange.Session!.Starts);
        Assert.Equal(0, exchange.Session.Renewals);
        Assert.Same(connection, exchange.Session.Connection);
    }

    [Fact]
    public async Task AMailboxThatIsNotThereIsANotFoundError()
    {
        await using var exchange = new WindowExchange(() => SidecarFixtures.Session());
        BridgeMessage request = Sample("mailbox-access.json") with { Data = new JsonObject { ["mailbox"] = "nobody" } };

        BridgeMessage? reply = await Dispatcher(exchange: exchange).DispatchAsync(request, None);

        Assert.Equal("error", reply?.Type);
        Assert.Equal("not-found", reply?.Data?["code"]?.GetValue<string>());

        // The sidecar's hint names `m365-aegis mailbox list`; the window shows its own (task 057).
        Assert.Equal(SignInHints.Window.Failures[FailureKind.NotFound], reply?.Data?["hint"]?.GetValue<string>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("{}")]
    [InlineData("{\"mailbox\":\" \"}")]
    [InlineData("{\"mailbox\":\"finance@contoso.com\",\"user\":\"alice@contoso.com\"}")]
    [InlineData("{\"user\":42}")]
    public async Task AMailboxAccessRequestNeedsAMailboxOrAUserAndStartsNothingWithout(string? data)
    {
        await using var exchange = new WindowExchange(() => SidecarFixtures.Session());
        var request = new BridgeMessage(BridgeProtocol.Version, "10", "mailbox-access", data is null ? null : JsonNode.Parse(data));

        BridgeMessage? reply = await Dispatcher(exchange: exchange).DispatchAsync(request, None);

        Assert.Equal("error", reply?.Type);
        Assert.Equal("usage", reply?.Data?["code"]?.GetValue<string>());
        Assert.Null(exchange.Session);
    }

    [Fact]
    public async Task AMailboxRequestWithoutAnExchangeSessionIsSidecarUnavailable()
    {
        BridgeMessage? reply = await Dispatcher().DispatchAsync(Sample("mailbox-list.json"), None);

        Assert.Equal("error", reply?.Type);
        Assert.Equal("sidecar-unavailable", reply?.Data?["code"]?.GetValue<string>());
        Assert.Equal(SignInHints.Window.Failures[FailureKind.SidecarUnavailable], reply?.Data?["hint"]?.GetValue<string>());
    }

    [Fact]
    public async Task TheWindowsListAnswersFromTheAdminApiAndTheAccessFromBothLayersOverOneToken()
    {
        await using WindowExchange exchange = Layers();
        BridgeDispatcher dispatcher = Dispatcher(exchange: exchange);

        BridgeMessage? list = await dispatcher.DispatchAsync(Sample("mailbox-list.json"), None);
        string afterList = exchange.Describe();
        ExchangeSession? sessionAfterList = exchange.Session;
        BridgeMessage? access = await dispatcher.DispatchAsync(Sample("mailbox-access.json"), None);

        AssertIsSample("mailboxes.json", list);
        Assert.Null(sessionAfterList);
        Assert.Equal("no sidecar, token acquisitions 1", afterList);
        AssertIsSample("mailbox-holders.json", access);
        Assert.Equal(1, exchange.Session!.Starts);
        Assert.Equal(1, exchange.Tokens!.Acquisitions);
        Assert.EndsWith(", token acquisitions 1", exchange.Describe(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ASignOutDropsTheWindowsTokenWithItsSession()
    {
        var broker = new FakeTokenBroker();
        broker.Accounts.Add(FakeTokenBroker.Alice);
        await using WindowExchange exchange = Layers();
        BridgeDispatcher dispatcher = Dispatcher(broker, exchange: exchange);
        await dispatcher.DispatchAsync(Sample("mailbox-access.json"), None);
        ExchangeTokenSource? before = exchange.Tokens;

        await dispatcher.DispatchAsync(Sample("logout.json"), None);
        ExchangeTokenSource? between = exchange.Tokens;
        BridgeMessage? again = await dispatcher.DispatchAsync(Sample("mailbox-list.json"), None);

        Assert.NotNull(before);
        Assert.Null(between);
        Assert.Equal("mailboxes", again?.Type);
        Assert.NotSame(before, exchange.Tokens);
        Assert.Equal(1, exchange.Tokens!.Acquisitions);
        Assert.Null(exchange.Session);
    }

    [Fact]
    public async Task AWindowWithoutAnAdminApiListsOnItsSession()
    {
        await using WindowExchange exchange = Layers(adminApi: false);

        BridgeMessage? reply = await Dispatcher(exchange: exchange).DispatchAsync(Sample("mailbox-list.json"), None);

        AssertIsSample("mailboxes.json", reply);
        Assert.Equal(1, exchange.Session!.Starts);
        await using ExchangeBackends backends = exchange.Backends();
        Assert.False(backends.HasAdminApi);
    }

    [Fact]
    public void EverySampleIsAMessageOfTheProtocol()
    {
        string[] samples = Directory.GetFiles(Contracts, "*.json");

        Assert.Equal(
            [
                "access-report.json", "access.json", "apply-plan.json", "device-code.json", "error.json", "group-members.json",
                "logged-out.json", "login.json", "logout.json", "mailbox-access-grant.json", "mailbox-access-revoke.json",
                "mailbox-access.json", "mailbox-forwarding-clear.json", "mailbox-forwarding-set.json", "mailbox-holders.json",
                "mailbox-list.json", "mailboxes.json", "members.json", "no-snapshot.json", "open-sign-in-page.json", "ping.json",
                "plan-applied.json", "plan.group-owners-remove.json", "plan.json", "pong.json", "ready.json",
                "sign-in-page-opened.json", "signed-in.json", "signed-out.json", "user-grants.json", "whoami.json",
            ],
            samples.Select(Path.GetFileName).Order(StringComparer.Ordinal));
        Assert.All(samples, path => Assert.NotNull(BridgeProtocol.TryParse(File.ReadAllText(path))));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("""{"aegis":2,"type":"ping"}""")]
    [InlineData("""{"type":"ping"}""")]
    [InlineData("""{"aegis":1}""")]
    public void ForeignTextIsRefused(string text)
    {
        Assert.Null(BridgeProtocol.TryParse(text));
    }

    [Fact]
    public void AMessageRoundTrips()
    {
        var message = new BridgeMessage(BridgeProtocol.Version, "42", "pong", new JsonObject { ["version"] = "1.2.3" });

        string json = BridgeProtocol.Write(message);
        BridgeMessage? parsed = BridgeProtocol.TryParse(json);

        Assert.Equal("""{"aegis":1,"id":"42","type":"pong","data":{"version":"1.2.3"}}""", json);
        Assert.NotNull(parsed);
        Assert.Equal(message.Type, parsed.Type);
        Assert.Equal(message.Id, parsed.Id);
    }

    private static BridgeMessage Sample(string fileName) =>
        BridgeProtocol.TryParse(File.ReadAllText(Path.Combine(Contracts, fileName)))
        ?? throw new InvalidOperationException($"{fileName} is not a message of the protocol");

    /// <summary>Collects the host's notifications of a request.</summary>
    private static Func<BridgeMessage, Task> Collect(List<BridgeMessage> notifications) => notification =>
    {
        notifications.Add(notification);
        return Task.CompletedTask;
    };

    /// <summary>
    /// The message, written as the host sends it, has the sample's type, no id when the sample has none, and the
    /// sample's payload fields: a notification whose values (the code's expiry) are the moment's own.
    /// </summary>
    private static void AssertHasTheShapeOf(string fileName, BridgeMessage message)
    {
        JsonObject expected = JsonNode.Parse(File.ReadAllText(Path.Combine(Contracts, fileName)))!.AsObject();
        JsonObject actual = JsonNode.Parse(BridgeProtocol.Write(message))!.AsObject();
        Assert.Equal(expected.Select(property => property.Key), actual.Select(property => property.Key));
        Assert.Equal(expected["type"]!.GetValue<string>(), actual["type"]!.GetValue<string>());
        Assert.Equal(
            expected["data"]!.AsObject().Select(property => property.Key),
            actual["data"]!.AsObject().Select(property => property.Key));
    }

    /// <summary>The reply, written as the host sends it, is the sample message, field by field (with the given id).</summary>
    private static void AssertIsSample(string fileName, BridgeMessage? reply, string? id = null)
    {
        JsonNode expected = SampleNode(fileName);
        if (id is not null)
        {
            expected["id"] = id;
        }

        AssertIs(expected, reply, fileName);
    }

    /// <summary>The reply, written as the host sends it, is the expected message, field by field.</summary>
    private static void AssertIs(JsonNode expected, BridgeMessage? reply, string what = "the expected message")
    {
        Assert.NotNull(reply);
        JsonNode actual = JsonNode.Parse(BridgeProtocol.Write(reply))!;
        Assert.True(JsonNode.DeepEquals(expected, actual), $"{what} differs from the reply:\n{actual.ToJsonString()}");
    }

    private static JsonNode SampleNode(string fileName) => JsonNode.Parse(File.ReadAllText(Path.Combine(Contracts, fileName)))!;

    /// <summary>A <c>mailbox-holders</c> message as the session alone answers it: every layer <c>pwsh</c> (task 063).</summary>
    private static JsonNode ThroughTheSession(JsonNode message)
    {
        message["data"]!["mailboxLayer"] = Backend.Pwsh;
        foreach (JsonNode? holder in message["data"]!["holders"]!.AsArray())
        {
            holder!["layer"] = Backend.Pwsh;
        }

        return message;
    }

    /// <summary>The payload of a <c>mailbox-holders</c> message, read back as the core's result.</summary>
    private static MailboxAccess Holders(BridgeMessage? message) =>
        message?.Data.Deserialize<MailboxAccess>(EnvelopeJson.Options) ?? throw new InvalidOperationException("no mailbox-holders payload");

    /// <summary>
    /// The window's layers as <see cref="BridgeServices.ForWindow"/> makes them (task 056), over one made-up token
    /// source: the session over the fake module and, unless <paramref name="adminApi"/> is false, the Admin API over the
    /// recorded answers, both asking that source.
    /// </summary>
    private static WindowExchange Layers(bool adminApi = true) => new(
        () => new ExchangeTokenSource(_ => Task.FromResult(new ExchangeToken(
            "fake-token", DateTimeOffset.UtcNow.AddHours(1), ["Exchange.Manage", "Exchange.ManageV2"], AdminApiConnection.TestAccount, AdminApiConnection.TestTenantId))),
        tokens => SidecarFixtures.Session(tokens.GetAsync),
        tokens => adminApi ? AdminApiConnection.Create(tokens, new FixtureGraphTransport(GraphFixtures.Root), null) : null);

    /// <summary>A home folder with one profile, the default: alice in contoso.</summary>
    private static string WithProfile()
    {
        string home = ProfileStoreTests.TempHome();
        new ProfileStore(home).Save(new TenantProfile("work", "contoso.onmicrosoft.com", "00000000-0000-0000-0000-000000000010", "alice@contoso.com"));
        return home;
    }

    /// <summary>
    /// The dispatcher over a scripted sign-in with the window's hints (as <see cref="BridgeServices.ForWindow"/> makes
    /// it), the recorded Graph answers and, when given, a window's Exchange session.
    /// </summary>
    private static BridgeDispatcher Dispatcher(FakeTokenBroker? broker = null, string? home = null, WindowExchange? exchange = null)
    {
        home ??= WithProfile();
        broker ??= new FakeTokenBroker();
        (GraphClient graph, _) = GraphFixtures.Client();
        return new BridgeDispatcher(new BridgeServices(
            new AuthService(home, _ => broker, SignInHints.Window),
            null,
            _ => graph,
            exception => InternalError.WriteToLog(exception, home))
        {
            Exchange = exchange,
        });
    }
}
