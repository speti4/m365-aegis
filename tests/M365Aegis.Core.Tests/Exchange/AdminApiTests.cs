using System.Net;
using System.Text.Json;
using M365Aegis.Core.Exchange;
using M365Aegis.Core.Exchange.AdminApi;
using M365Aegis.Core.Exchange.Sidecar;
using M365Aegis.Core.Graph;
using M365Aegis.Core.Output;
using M365Aegis.Core.Tests.Graph;
using M365Aegis.Tests;

namespace M365Aegis.Core.Tests.Exchange;

/// <summary>
/// The Exchange Online Admin API backend of task 042 over the recorded answers under
/// tests/fixtures/graph/outlook.office365.com: the request it sends, the paging, the throttling, the failures.
/// </summary>
public sealed class AdminApiTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static (AdminApiClient Client, FixtureGraphTransport Transport, List<TimeSpan> Delays) Client()
    {
        var delays = new List<TimeSpan>();
        var transport = new FixtureGraphTransport(GraphFixtures.Root);
        var options = new GraphClientOptions
        {
            Delay = (delay, _) =>
            {
                delays.Add(delay);
                return Task.CompletedTask;
            },
            Jitter = () => 0,
        };
        var client = new AdminApiClient(transport, _ => Task.FromResult(new AdminApiTarget(AdminApiConnection.TestTenantId, AdminApiConnection.TestAccount)), null, options);
        return (client, transport, delays);
    }

    [Fact]
    public async Task ACmdletIsAPostWithItsInputTheAnchorAndTheTenantInTheUrl()
    {
        (AdminApiClient client, FixtureGraphTransport transport, _) = Client();

        ExoResult result = await client.InvokeAsync(new ExoCommand(AdminApiCmdlets.GetOrganizationConfig), Ct);

        ExoObject config = Assert.Single(result.Items);
        Assert.Equal(25, config.Number("MailTipsLargeAudienceThreshold"));
        GraphHttpRequest sent = Assert.Single(transport.Requests);
        Assert.Equal(HttpMethod.Post, sent.Method);
        Assert.Equal($"https://outlook.office365.com/adminapi/v2.0/{AdminApiConnection.TestTenantId}/OrganizationConfig", sent.Uri.AbsoluteUri);
        Assert.Equal("UPN:alice@contoso.com", sent.Headers["X-AnchorMailbox"]);
        Assert.Equal("""{"CmdletInput":{"CmdletName":"Get-OrganizationConfig"}}""", sent.Body);
        Assert.Equal(["00000000-0000-0000-0000-0000000000c1"], client.RequestLog.Ids);
    }

    [Fact]
    public async Task ParametersGoIntoTheBodyAndPropertiesIntoSelect()
    {
        var transport = new ScriptedTransport(_ => new GraphResponse(200, [], """{"value":[]}"""));
        var client = new AdminApiClient(transport, _ => Task.FromResult(new AdminApiTarget(AdminApiConnection.TestTenantId, AdminApiConnection.TestAccount)));

        await client.InvokeAsync(
            new ExoCommand(AdminApiCmdlets.GetMailbox)
            {
                Parameters = new Dictionary<string, object?> { ["Identity"] = "finance", ["ResultSize"] = 10, ["IncludeGrantSendOnBehalfToWithDisplayNames"] = true },
                Properties = ["DisplayName", "PrimarySmtpAddress"],
            },
            Ct);

        GraphHttpRequest sent = Assert.Single(transport.Requests);
        Assert.Equal("?$select=DisplayName%2CPrimarySmtpAddress", sent.Uri.Query);
        using JsonDocument body = JsonDocument.Parse(sent.Body!);
        JsonElement parameters = body.RootElement.GetProperty("CmdletInput").GetProperty("Parameters");
        Assert.Equal("finance", parameters.GetProperty("Identity").GetString());
        Assert.Equal(10, parameters.GetProperty("ResultSize").GetInt32());
        Assert.True(parameters.GetProperty("IncludeGrantSendOnBehalfToWithDisplayNames").GetBoolean());
    }

    [Fact]
    public async Task EveryPageIsReadByPostingTheSameBodyToTheNextLink()
    {
        (AdminApiClient client, FixtureGraphTransport transport, _) = Client();

        ExoResult result = await client.InvokeAsync(
            new ExoCommand(AdminApiCmdlets.GetDistributionGroupMember) { Parameters = new Dictionary<string, object?> { ["Identity"] = "sales", ["ResultSize"] = "Unlimited" } },
            Ct);

        Assert.Equal(["Bob Example", "Dave Private", "Alice Example"], result.Items.Select(item => item.Text("DisplayName")));
        Assert.Equal(2, transport.Requests.Count);
        Assert.Equal(transport.Requests[0].Body, transport.Requests[1].Body);
        Assert.Equal("?$skiptoken=page-2", transport.Requests[1].Uri.Query);
        Assert.Equal(2, client.Calls);
    }

    [Fact]
    public async Task AThrottledCallWaitsItsRetryAfterAndIsSentAgain()
    {
        (AdminApiClient client, FixtureGraphTransport transport, List<TimeSpan> delays) = Client();

        ExoResult result = await client.InvokeAsync(
            new ExoCommand(AdminApiCmdlets.GetDistributionGroupMember) { Parameters = new Dictionary<string, object?> { ["Identity"] = "busy", ["ResultSize"] = "Unlimited" } },
            Ct);

        Assert.Empty(result.Items);
        Assert.Equal([TimeSpan.FromSeconds(2)], delays);
        Assert.Equal(2, transport.Requests.Count);
    }

    [Theory]
    [InlineData("nobody", true, FailureKind.NotFound)]
    [InlineData("bob", true, FailureKind.Forbidden)]
    [InlineData("alice", true, FailureKind.CmdletFailed)]
    public async Task AFailedCallIsTheContractsFailure(string identity, bool displayNames, FailureKind expected)
    {
        (AdminApiClient client, _, _) = Client();
        var parameters = new Dictionary<string, object?> { ["Identity"] = identity };
        if (displayNames)
        {
            parameters["IncludeGrantSendOnBehalfToWithDisplayNames"] = true;
        }

        AegisException exception = await Assert.ThrowsAsync<AegisException>(() => client.InvokeAsync(new ExoCommand(AdminApiCmdlets.GetMailbox) { Parameters = parameters }, Ct));

        Assert.Equal(expected, exception.Failure.Kind);
        Assert.DoesNotContain("PROD.OUTLOOK.COM", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AFourHundredThatSaysNotFoundIsNotFound()
    {
        (AdminApiClient client, _, _) = Client();

        AegisException exception = await Assert.ThrowsAsync<AegisException>(() => client.InvokeAsync(
            new ExoCommand(AdminApiCmdlets.GetMailboxFolderPermission) { Parameters = new Dictionary<string, object?> { ["Identity"] = "finance:\\Missing", ["ResultSize"] = "Unlimited" } },
            Ct));

        Assert.Equal(FailureKind.NotFound, exception.Failure.Kind);
        Assert.Equal(400, exception.HttpStatus);
    }

    [Theory]
    [InlineData("Set-Mailbox")]
    [InlineData("Get-EXOMailbox")]
    [InlineData("Invoke-Command")]
    public async Task ACmdletOffTheListIsRefusedBeforeAnythingIsSent(string cmdlet)
    {
        var transport = new ScriptedTransport(_ => throw new InvalidOperationException("not sent"));
        var client = new AdminApiClient(transport, _ => throw new InvalidOperationException("no target needed"));

        AegisException exception = await Assert.ThrowsAsync<AegisException>(() => client.InvokeAsync(new ExoCommand(cmdlet), Ct));

        Assert.Equal(ExitCode.CmdletNotAllowed, exception.Failure.ExitCode);
        Assert.Empty(transport.Requests);
    }

    [Fact]
    public async Task PipedInputIsABug()
    {
        (AdminApiClient client, _, _) = Client();

        await Assert.ThrowsAsync<ArgumentException>(() => client.InvokeAsync(new ExoCommand(AdminApiCmdlets.GetMailbox) { Input = ["finance"] }, Ct));
    }

    [Fact]
    public async Task ANextLinkOffTheAdminApiHostIsRefused()
    {
        var transport = new ScriptedTransport(_ => new GraphResponse(200, [], """{"value":[{"Name":"x"}],"@odata.nextLink":"https://example.com/next"}"""));
        var client = new AdminApiClient(transport, _ => Task.FromResult(new AdminApiTarget(AdminApiConnection.TestTenantId, AdminApiConnection.TestAccount)));

        AegisException exception = await Assert.ThrowsAsync<AegisException>(() => client.InvokeAsync(new ExoCommand(AdminApiCmdlets.GetAcceptedDomain), Ct));

        Assert.Equal(FailureKind.ServiceError, exception.Failure.Kind);
        Assert.Single(transport.Requests);
    }

    [Fact]
    public async Task TheTransportSendsTheTokenToTheAdminApiHostOnly()
    {
        var handler = new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
        var transport = new HttpAdminApiTransport(_ => Task.FromResult("token-of-the-tests"), handler);
        var anchor = new Dictionary<string, string> { ["X-AnchorMailbox"] = "UPN:alice@contoso.com" };

        await transport.SendAsync(new GraphHttpRequest(HttpMethod.Post, new Uri("https://outlook.office365.com/adminapi/v2.0/x/OrganizationConfig"), anchor, "{}"), Ct);
        await Assert.ThrowsAsync<InvalidOperationException>(() => transport.SendAsync(new GraphHttpRequest(HttpMethod.Post, new Uri("https://graph.microsoft.com/v1.0/me"), anchor, "{}"), Ct));

        HttpRequestMessage sent = Assert.Single(handler.Requests);
        Assert.Equal("token-of-the-tests", sent.Headers.Authorization?.Parameter);
        Assert.Equal("UPN:alice@contoso.com", Assert.Single(sent.Headers.GetValues("X-AnchorMailbox")));
        Assert.Equal("application/json", sent.Content?.Headers.ContentType?.MediaType);
    }

    [Theory]
    [InlineData("(bob@contoso.com, Bob Example)", "Bob Example", "bob@contoso.com")]
    [InlineData("Bob Example <bob@contoso.com>", "Bob Example", "bob@contoso.com")]
    [InlineData("Bob Example (bob@contoso.com)", "Bob Example", "bob@contoso.com")]
    [InlineData("bob@contoso.com", "bob@contoso.com", "bob@contoso.com")]
    [InlineData("SMTP:bob@contoso.com", "bob@contoso.com", "bob@contoso.com")]
    [InlineData("Bob Example", "Bob Example", null)]
    [InlineData("""{"DisplayName":"Bob Example","PrimarySmtpAddress":"bob@contoso.com"}""", "Bob Example", "bob@contoso.com")]
    [InlineData("""{"Name":"bob","Identity":"bob@contoso.com"}""", "bob", "bob@contoso.com")]
    public void ADelegateIsReadFromAnObjectOrAText(string entry, string holder, string? address)
    {
        (string Holder, string? Address) read = MailboxLookups.Delegate(entry);

        Assert.Equal(holder, read.Holder);
        Assert.Equal(address, read.Address);
    }

    [Fact]
    public void TheFixturesNameAnAdminApiCallByItsParameters()
    {
        var uri = new Uri("https://outlook.office365.com/adminapi/v2.0/t/Mailbox?$skiptoken=page-2");
        const string body = """{"CmdletInput":{"CmdletName":"Get-Mailbox","Parameters":{"ResultSize":"Unlimited","Identity":"finance:\\Calendar"}}}""";

        Assert.Equal("outlook.office365.com/adminapi/v2.0/t/Mailbox@$skiptoken=page-2&Identity=finance%3A%5CCalendar&ResultSize=Unlimited", FixtureGraphTransport.RelativePath(uri, body));
        // A Graph call's body never names its fixture.
        Assert.Equal("graph.microsoft.com/v1.0/$batch", FixtureGraphTransport.RelativePath(new Uri("https://graph.microsoft.com/v1.0/$batch"), body));
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        internal List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(answer(request));
        }
    }
}
