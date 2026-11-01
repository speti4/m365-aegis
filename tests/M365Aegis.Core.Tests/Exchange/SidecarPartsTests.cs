using System.Net;
using System.Text;
using System.Text.Json;
using M365Aegis.Core.Config;
using M365Aegis.Core.Exchange;
using M365Aegis.Core.Exchange.Sidecar;
using M365Aegis.Core.Graph;
using M365Aegis.Core.Output;
using M365Aegis.Tests;

namespace M365Aegis.Core.Tests.Exchange;

/// <summary>The parts of the sidecar that need no child process: the error mapping, the values, the organization, the probes.</summary>
public sealed class SidecarPartsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("invoke", "cmdlet-failed", "The operation couldn't be performed because object 'x' couldn't be found on 'contoso.onmicrosoft.com'.", null, FailureKind.NotFound)]
    [InlineData("invoke", "cmdlet-failed", "Error while querying REST service. HttpStatusCode=404 ErrorCode=0 Message=Not Found", null, FailureKind.NotFound)]
    [InlineData("invoke", "cmdlet-failed", "something", "ManagementObjectNotFoundException", FailureKind.NotFound)]
    [InlineData("invoke", "cmdlet-failed", "The request was throttled. HttpStatusCode=429", null, FailureKind.Throttled)]
    [InlineData("invoke", "cmdlet-failed", "Server Busy: try again", null, FailureKind.Throttled)]
    [InlineData("invoke", "cmdlet-failed", "HttpStatusCode=403 Forbidden", null, FailureKind.Forbidden)]
    [InlineData("invoke", "cmdlet-failed", "UnAuthorized (401): the token has expired", null, FailureKind.Auth)]
    [InlineData("invoke", "cmdlet-failed", "A parameter cannot be found that matches parameter name 'Frobnicate'.", null, FailureKind.CmdletFailed)]
    [InlineData("invoke", "cmdlet-failed", "The term 'Get-DistributionGroup' is not recognized as a name of a cmdlet, function, script file, or executable program.", "CommandNotFoundException", FailureKind.Forbidden)]
    [InlineData("invoke", "cmdlet-failed", "something", "CommandNotFoundException", FailureKind.Forbidden)]
    [InlineData("connect", "connect-failed", "The term 'Connect-ExchangeOnline' is not recognized as a name of a cmdlet, function, script file, or executable program.", "CommandNotFoundException", FailureKind.SidecarUnavailable)]
    [InlineData("invoke", "cmdlet-not-allowed", "'Remove-Mailbox' is not on the sidecar's allow-list", null, FailureKind.CmdletNotAllowed)]
    [InlineData("connect", "connect-failed", "AADSTS50013: Assertion failed signature validation", null, FailureKind.Auth)]
    [InlineData("connect", "connect-failed", "Access denied: the user has no Exchange role", null, FailureKind.Forbidden)]
    [InlineData("connect", "connect-failed", "fake: unknown organization 'fabrikam.onmicrosoft.com'", null, FailureKind.SidecarUnavailable)]
    [InlineData("reconnect", "connect-failed", "the remote name could not be resolved", null, FailureKind.SidecarUnavailable)]
    [InlineData("connect", "module-missing", "the ExchangeOnlineManagement module is not installed", null, FailureKind.SidecarUnavailable)]
    public void AnErrorReplyIsTheFailureItNames(string type, string code, string message, string? errorId, FailureKind kind)
    {
        Exception exception = SidecarFailures.For(type, new SidecarError(code, message, errorId, null), "");

        Assert.Equal(kind, Assert.IsType<AegisException>(exception).Failure.Kind);
    }

    [Fact]
    public void AHiddenCmdletIsARefusedRoleWithTheReasonInTheHint()
    {
        // Task 108: a session without an Exchange Online role has the cmdlet left out, which task 096's fallback catches as a refusal.
        Exception exception = SidecarFailures.For(
            "invoke",
            new SidecarError("cmdlet-failed", "The term 'Get-DistributionGroup' is not recognized as a name of a cmdlet, function, script file, or executable program.", "CommandNotFoundException", "ObjectNotFound"),
            "");

        AegisFailure failure = Assert.IsType<AegisException>(exception).Failure;
        Assert.Equal((FailureKind.Forbidden, ExitCode.Forbidden), (failure.Kind, failure.ExitCode));
        Assert.Equal("the Exchange Online cmdlet failed: The term 'Get-DistributionGroup' is not recognized as a name of a cmdlet, function, script file, or executable program.", failure.Message);
        Assert.StartsWith("Exchange Online imports only the cmdlets the account's roles allow, so the session has no such cmdlet; the account needs an Exchange Online role", failure.Hint, StringComparison.Ordinal);
    }

    [Fact]
    public void ARestErrorWithAJsonBodyIsShortenedToItsSentence()
    {
        const string raw = """
            Error while querying REST service. HttpStatusCode=404 ErrorMessage={"error":{"code":"NotFound","message":"Error executing request. The operation couldn't be performed because object 'nobody@contoso.com' couldn't be found on 'XXXPR01A001DC001.EURPR01A001.PROD.OUTLOOK.COM'.","details":[{"code":"Context","message":"Ex6F9304|Microsoft.Exchange.Configuration.Tasks.ManagementObjectNotFoundException"}],"innererror":{"message":"…","type":"Microsoft.Exchange.Configuration.Tasks.ManagementObjectNotFoundException"}}} ErrorCode= …
            """;

        Exception exception = SidecarFailures.For("invoke", new SidecarError("cmdlet-failed", raw, null, null), "");

        AegisFailure failure = Assert.IsType<AegisException>(exception).Failure;
        Assert.Equal(FailureKind.NotFound, failure.Kind);
        Assert.Equal("The operation couldn't be performed because object 'nobody@contoso.com' couldn't be found.", failure.Message);
    }

    [Theory]
    [InlineData("plain text", "plain text")]
    [InlineData("HttpStatusCode=500 ErrorMessage={\"error\":{\"message\":\"Server \\\"busy\\\"\"}}", "Server \"busy\"")]
    [InlineData("HttpStatusCode=500 ErrorMessage={\"error\":{\"message\":\"cut off", "HttpStatusCode=500 ErrorMessage={\"error\":{\"message\":\"cut off")]
    [InlineData("  two\n lines  ", "two lines")]
    public void AServiceMessageIsOneLine(string raw, string expected) =>
        Assert.Equal(expected, SidecarFailures.Shorten(raw));

    [Fact]
    public void AVeryLongMessageIsCut()
    {
        string shortened = SidecarFailures.Shorten(new string('x', 1000));

        Assert.Equal(300, shortened.Length);
        Assert.EndsWith("…", shortened, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("bad-request")]
    [InlineData("not-connected")]
    [InlineData("unknown-type")]
    public void AnErrorThatMeansTheCoreSentAWrongMessageIsABug(string code)
    {
        Exception exception = SidecarFailures.For("invoke", new SidecarError(code, "no", null, null), "a line of stderr");

        Assert.IsType<InvalidOperationException>(exception);
        Assert.Contains("a line of stderr", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnObjectReadsItsPlainValues()
    {
        using JsonDocument document = JsonDocument.Parse("""
            {"Name":"finance","Count":40960,"Shared":true,"Flag":"False","Big":"12","List":["a",null,"b"],"Empty":"","Null":null}
            """);
        var item = new ExoObject(document.RootElement);

        Assert.Equal("finance", item.Text("name"));
        Assert.Equal("40960", item.Text("Count"));
        Assert.Equal(40960, item.Number("Count"));
        Assert.Equal(12, item.Number("Big"));
        Assert.True(item.Flag("Shared"));
        Assert.False(item.Flag("Flag"));
        Assert.Equal(["a", "b"], item.Texts("List"));
        Assert.Equal("a, b", item.Text("List"));
        Assert.Null(item.Text("Empty"));
        Assert.Null(item.Text("Null"));
        Assert.Null(item.Text("Missing"));
        Assert.Empty(item.Texts("Missing"));
        Assert.Equal(["finance"], item.Texts("Name"));
        Assert.Null(item.Flag("Name"));
        Assert.Null(item.Number("Name"));
    }

    [Fact]
    public void AnInvokeMessageCarriesOnlyWhatWasGiven()
    {
        string bare = JsonSerializer.Serialize(new ExoCommand(ExoCmdlets.GetAcceptedDomain).ToMessageData());
        string full = JsonSerializer.Serialize(new ExoCommand(ExoCmdlets.GetMailboxStatistics) { Input = ["alice"], Properties = ["ItemCount"] }.ToMessageData());

        Assert.Equal("""{"cmdlet":"Get-AcceptedDomain","parameters":{}}""", bare);
        Assert.Equal("""{"cmdlet":"Get-EXOMailboxStatistics","parameters":{},"properties":["ItemCount"],"input":["alice"]}""", full);
    }

    [Fact]
    public void OnlyTheListedCmdletsAreAllowed()
    {
        Assert.True(ExoCmdlets.IsAllowed("get-exomailbox"));
        Assert.False(ExoCmdlets.IsAllowed("Remove-Mailbox"));
        Assert.False(ExoCmdlets.IsAllowed(null));
        AegisException exception = Assert.Throws<AegisException>(() => ExoCmdlets.EnsureAllowed("Invoke-Expression"));
        Assert.Equal(12, (int)exception.Failure.ExitCode);
    }

    [Fact]
    public async Task AnOnmicrosoftTenantIsTheOrganizationWithoutACall()
    {
        var profile = new TenantProfile("work", "contoso.onmicrosoft.com", "00000000-0000-0000-0000-000000000010", "alice@contoso.com");

        string organization = await ExchangeConnection.OrganizationAsync(profile, () => throw new InvalidOperationException("no Graph call expected"), Ct);

        Assert.Equal("contoso.onmicrosoft.com", organization);
    }

    [Fact]
    public async Task AnotherTenantIsResolvedToItsInitialDomainThroughGraph()
    {
        var profile = new TenantProfile("work", "00000000-0000-0000-0000-000000000002", "00000000-0000-0000-0000-000000000010", "alice@contoso.com");
        (GraphClient graph, _) = GraphFixtures.Client();

        string organization = await ExchangeConnection.OrganizationAsync(profile, () => graph, Ct);

        Assert.Equal("contoso.onmicrosoft.com", organization);
        Assert.Single(graph.RequestLog.Ids);
    }

    [Fact]
    public async Task TheConnectProbeTimesTheConnectTheCmdletAndTheReconnect()
    {
        await using ExchangeSession session = SidecarFixtures.Session();

        ConnectProbeReport report = await ExchangeProbes.ConnectAsync(session, ExchangeConnectWith.Organization, TimeProvider.System, Ct);

        Assert.Equal("Organization", report.ConnectWith);
        Assert.Equal("3.10.1", report.Module);
        Assert.True(report.StartMs > 0);
        Assert.True(report.TokenMinutesLeft is > 50 and <= 60);
        Assert.True(report.CarriesManageAndManageV2);
        Assert.Equal(1, session.Renewals);
    }

    [Fact]
    public async Task TheAdminApiProbeSendsTheDocumentedRequestAndKeepsOnlyTheShape()
    {
        var handler = new ScriptedHandler(request => request.RequestUri!.AbsolutePath.EndsWith("/OrganizationConfig", StringComparison.Ordinal)
            ? (HttpStatusCode.OK, """{"@odata.context":"https://outlook.office365.com/adminapi/v2.0/$metadata#OrganizationConfig","value":[{"Name":"contoso.onmicrosoft.com","MailTipsAllTipsEnabled":true,"MailTipsLargeAudienceThreshold":25}]}""")
            : (HttpStatusCode.OK, """{"value":[{"DisplayName":"Alice Example"}],"@odata.nextLink":"https://outlook.office365.com/adminapi/v2.0/00000000-0000-0000-0000-000000000002/Mailbox?$skiptoken=abc&$select=DisplayName"}"""));
        using var http = new HttpClient(handler);

        AdminApiProbeReport report = await ExchangeProbes.AdminApiAsync(http, SidecarFixtures.Token(DateTimeOffset.UtcNow.AddHours(1)), Ct);

        Assert.True(report.Answered);
        Assert.Equal(2, report.Calls.Count);
        AdminApiCall organizationConfig = report.Calls[0];
        Assert.Equal(200, organizationConfig.Status);
        Assert.Equal(1, organizationConfig.Items);
        Assert.Contains("value[0].MailTipsAllTipsEnabled: boolean", organizationConfig.Shape);
        Assert.DoesNotContain(organizationConfig.Shape, line => line.Contains("contoso", StringComparison.Ordinal));
        Assert.Equal(["$skiptoken", "$select"], report.Calls[1].NextLinkParameters);

        HttpRequestMessage first = handler.Requests[0];
        Assert.Equal(HttpMethod.Post, first.Method);
        Assert.Equal("https://outlook.office365.com/adminapi/v2.0/00000000-0000-0000-0000-000000000002/OrganizationConfig", first.RequestUri!.ToString());
        Assert.Equal("UPN:alice@contoso.com", first.Headers.GetValues("X-AnchorMailbox").Single());
        Assert.Equal("Bearer", first.Headers.Authorization!.Scheme);
        Assert.Equal("""{"CmdletInput":{"CmdletName":"Get-OrganizationConfig"}}""", handler.Bodies[0]);
    }

    [Fact]
    public async Task TheAdminApiProbeTriesTheOtherAnchorFormWhenTheFirstIsRefused()
    {
        var handler = new ScriptedHandler(request => (HttpStatusCode.Forbidden, """{"error":{"code":"ErrorAccessDenied","message":"Access is denied."}}"""));
        using var http = new HttpClient(handler);

        AdminApiProbeReport report = await ExchangeProbes.AdminApiAsync(http, SidecarFixtures.Token(DateTimeOffset.UtcNow.AddHours(1)), Ct);

        Assert.False(report.Answered);
        Assert.Equal(["UPN:", "AAD-UPN:", "UPN:"], report.Calls.Select(call => call.AnchorForm));
        Assert.Equal("ErrorAccessDenied", report.Calls[0].ErrorCode);
        Assert.Equal("AAD-UPN:alice@contoso.com", handler.Requests[1].Headers.GetValues("X-AnchorMailbox").Single());
    }

    [Fact]
    public async Task TheAdminApiProbeShowsTheFormOfTheDelegatesAndNoValue()
    {
        var handler = new ScriptedHandler(request => request.RequestUri!.AbsolutePath.EndsWith("/OrganizationConfig", StringComparison.Ordinal)
            ? (HttpStatusCode.OK, """{"value":[{"Name":"contoso.onmicrosoft.com"}]}""")
            : (HttpStatusCode.OK, """{"value":[{"DisplayName":"Finance","GrantSendOnBehalfToWithDisplayNames":["Bob Example <bob@contoso.com>"],"User":{"DisplayName":"Alice Example","UserType":"Internal"}}]}"""));
        using var http = new HttpClient(handler);

        AdminApiProbeReport report = await ExchangeProbes.AdminApiAsync(
            http, SidecarFixtures.Token(DateTimeOffset.UtcNow.AddHours(1)), new AdminApiProbeTargets("finance@contoso.com", null, "sales@contoso.com"), Ct);

        Assert.Equal(["OrganizationConfig", "Mailbox", "Mailbox", "MailboxFolderPermission", "DistributionGroupMember"], report.Calls.Select(call => call.Endpoint));
        AdminApiCall delegates = report.Calls[2];
        Assert.Contains("value[0].GrantSendOnBehalfToWithDisplayNames[0]: string 'a a <a@a.a>'", delegates.Shape);
        Assert.Contains("value[0].User: object {DisplayName,UserType}", delegates.Shape);
        Assert.DoesNotContain(report.Calls.SelectMany(call => call.Shape), line => line.Contains("contoso", StringComparison.Ordinal) || line.Contains("Bob", StringComparison.Ordinal));
        Assert.Contains("\"IncludeGrantSendOnBehalfToWithDisplayNames\":true", handler.Bodies[2], StringComparison.Ordinal);
        Assert.Contains("finance@contoso.com:\\\\Calendar", handler.Bodies[3], StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheForwardingProbeTriesEachWayAndSaysWhichFieldsCameBack()
    {
        var handler = new ScriptedHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/OrganizationConfig", StringComparison.Ordinal))
            {
                return (HttpStatusCode.OK, """{"value":[{"Name":"contoso.onmicrosoft.com"}]}""");
            }

            string body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return body.Contains("PropertySets", StringComparison.Ordinal)
                ? (HttpStatusCode.OK, """{"value":[{"DisplayName":"Bob Example","ForwardingSmtpAddress":"smtp:bob@fabrikam.com","ForwardingAddress":null,"DeliverToMailboxAndForward":true}]}""")
                : body.Contains("Properties", StringComparison.Ordinal)
                    ? (HttpStatusCode.BadRequest, """{"error":{"code":"InvalidParameter","message":"A parameter cannot be found that matches parameter name 'Properties'."}}""")
                    : (HttpStatusCode.OK, """{"value":[{"DisplayName":"Bob Example","PrimarySmtpAddress":"bob@contoso.com"}]}""");
        });
        using var http = new HttpClient(handler);

        AdminApiProbeReport report = await ExchangeProbes.AdminApiAsync(
            http,
            SidecarFixtures.Token(DateTimeOffset.UtcNow.AddHours(1)),
            new AdminApiProbeTargets(Forwarding: true, MailboxParameters: """{"PropertySets":["Minimum","Delivery"]}""", Select: "DisplayName,ForwardingSmtpAddress"),
            Ct);

        AdminApiCall[] variants = [.. report.Calls.Where(call => call.Endpoint.StartsWith("Mailbox (", StringComparison.Ordinal))];
        Assert.Equal(["Mailbox (select)", "Mailbox (properties)", "Mailbox (property-sets-delivery)", "Mailbox (property-sets-all)", "Mailbox (given)"], variants.Select(call => call.Endpoint));
        Assert.Equal([0, 0, 3, 3, 3], variants.Select(call => ExchangeProbes.ForwardingFieldsIn(call).Count));
        Assert.Equal(400, variants[1].Status);
        Assert.DoesNotContain(variants.SelectMany(call => call.Shape), line => line.Contains("fabrikam", StringComparison.Ordinal));
        Assert.Contains("$select=Identity%2CForwardingSmtpAddress", handler.Requests[2].RequestUri!.ToString(), StringComparison.Ordinal);
        Assert.Equal("""{"CmdletInput":{"CmdletName":"Get-Mailbox","Parameters":{"ResultSize":1,"PropertySets":["Minimum","Delivery"]}}}""", handler.Bodies[^1]);
    }

    [Theory]
    [InlineData("[1]")]
    [InlineData("{not json")]
    public void ProbeParametersThatAreNoJsonObjectAreAUsageError(string parameters)
    {
        AegisException exception = Assert.Throws<AegisException>(() => ExchangeProbes.MailboxBody("bob@contoso.com", parameters));

        Assert.Equal(FailureKind.Usage, exception.Failure.Kind);
    }

    [Theory]
    [InlineData("Bob Example <bob@contoso.com>", "a a <a@a.a>")]
    [InlineData("SMTP:bob2@contoso.com", "a:a9@a.a")]
    [InlineData("Zoë Ötvös", "a a")]
    public void APatternKeepsTheFormAndDropsTheValue(string text, string pattern) =>
        Assert.Equal(pattern, ExchangeProbes.Pattern(text));

    /// <summary>An HTTP handler that answers from a script and keeps the requests and their bodies.</summary>
    private sealed class ScriptedHandler(Func<HttpRequestMessage, (HttpStatusCode Status, string Body)> answer) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            Bodies.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken));
            (HttpStatusCode status, string body) = answer(request);
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }
}
