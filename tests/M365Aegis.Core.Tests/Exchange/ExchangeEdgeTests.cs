using System.Text.Json;
using M365Aegis.Core.Auth;
using M365Aegis.Core.Config;
using M365Aegis.Core.Diagnostics;
using M365Aegis.Core.Exchange;
using M365Aegis.Core.Exchange.AdminApi;
using M365Aegis.Core.Exchange.Sidecar;
using M365Aegis.Core.Graph;
using M365Aegis.Core.Output;
using M365Aegis.Core.Tests.Auth;
using M365Aegis.Core.Tests.Config;
using M365Aegis.Core.Tests.Graph;
using M365Aegis.Tests;

namespace M365Aegis.Core.Tests.Exchange;

/// <summary>
/// The edges of the Exchange Online parts that need no child process: the values of an object, the error mapping's
/// rarer inputs, the embedded script, the layers of a command, the token source, the connection's factories, the Admin
/// API's bodies, pages, retries and failures, and its HTTP transport's network failures.
/// </summary>
public sealed class ExchangeEdgeTests
{
    private static readonly string[] List = ["a", "b"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void AnObjectAnswersNullForWhatItDoesNotHold()
    {
        using JsonDocument document = JsonDocument.Parse("""{"Name":"finance","Size":1.5,"Count":"12","Flag":"yes","On":"True","Big":2e3,"Odd":true}""");
        var item = new ExoObject(document.RootElement);

        Assert.Equal(["Name", "Size", "Count", "Flag", "On", "Big", "Odd"], item.Names);
        Assert.Null(item.Value("Missing"));
        Assert.Equal(JsonValueKind.String, item.Value("Name")!.Value.ValueKind);
        Assert.Null(item.Flag("Missing"));
        Assert.Null(item.Flag("Flag"));
        Assert.True(item.Flag("On"));
        Assert.Null(item.Number("Missing"));
        Assert.Equal(1, item.Number("Size"));
        Assert.Equal(2000, item.Number("Big"));
        Assert.Equal(12, item.Number("Count"));
        Assert.Null(item.Number("Name"));
        Assert.Null(item.Number("Odd"));
    }

    [Fact]
    public void ABugReplyCarriesTheChildsStderr()
    {
        Exception exception = SidecarFailures.For("invoke", new SidecarError("bad-request", "no such field", null, null), "line one | line two");

        Assert.Equal("the sidecar refused 'invoke' (bad-request: no such field); stderr: line one | line two", exception.Message);
    }

    [Theory]
    [InlineData("Error while querying REST service. HttpStatusCode=400 ErrorMessage=no body at all", "Error while querying REST service. HttpStatusCode=400 ErrorMessage=no body at all")]
    [InlineData("ErrorMessage={\"error\":\"flat\",\"message\":\"the first message\"}", "the first message")]
    [InlineData("ErrorMessage={\"error\":{\"message\":42},\"x\":{\"message\":\"nested\"}}", "nested")]
    [InlineData("ErrorMessage={\"other\":1}", "ErrorMessage={\"other\":1}")]
    [InlineData("ErrorMessage={\"message\":\"cut \\\"short\\\" here\", oops}", "cut \"short\" here")]
    [InlineData("ErrorMessage={not json}", "ErrorMessage={not json}")]
    [InlineData("ErrorMessage=}{", "ErrorMessage=}{")]
    public void ABodyOfAnotherShapeGivesItsFirstMessageOrNothing(string raw, string expected) =>
        Assert.Equal(expected, SidecarFailures.Shorten(raw));

    [Fact]
    public void AServiceErrorIsWrittenWholeToTheVerboseLog()
    {
        string home = ProfileStoreTests.TempHome();
        using (LogSink.UseVerbose(LogSink.ForHome(home)))
        {
            SidecarFailures.For("invoke", new SidecarError("cmdlet-failed", "it broke", "Some.ErrorId", "InvalidOperation"), "");
        }

        string log = File.ReadAllText(Assert.Single(Directory.GetFiles(Path.Combine(home, "logs"))));
        Assert.Contains("exchange-online: invoke cmdlet-failed: it broke (error id: Some.ErrorId; category: InvalidOperation)", log, StringComparison.Ordinal);
    }

    [Fact]
    public void TheBuildCopyIsTheEmbeddedScriptAndAMissingResourceIsNull()
    {
        Assert.NotEmpty(SidecarScript.ReadBuildCopy()!);
        Assert.Null(SidecarScript.ReadResource("M365Aegis.Core.Sidecar.no-such-file.ps1"));
    }

    [Fact]
    public async Task TheLayersSayWhatTheRunHasAndWhatItUsed()
    {
        var sidecar = new RecordingRunner();
        await using var backends = new ExchangeBackends(sidecar);

        Assert.Equal(Backend.Pwsh, backends.Backend);
        Assert.False(backends.HasGraph);
        Assert.False(backends.CanStartSidecars);
        Assert.Throws<InvalidOperationException>(() => backends.Graph);
        Assert.Throws<InvalidOperationException>(() => backends.StartSidecar());
        Assert.Null(backends.AdminApi);
        Assert.Same(sidecar, backends.Sidecar);
        Assert.Equal(Backend.Pwsh, backends.Backend);
    }

    [Fact]
    public async Task ATokenSourceKeepsTheThresholdItIsGivenAndLogsEachAcquisition()
    {
        string home = ProfileStoreTests.TempHome();
        var tokens = new ExchangeTokenSource(_ => Task.FromResult(SidecarFixtures.Token(DateTimeOffset.UtcNow.AddHours(1))), renewBefore: TimeSpan.FromMinutes(1));

        Assert.Equal(TimeSpan.FromMinutes(1), tokens.RenewBefore);
        using (LogSink.UseVerbose(LogSink.ForHome(home)))
        {
            await tokens.GetAsync(Ct);
        }

        string log = File.ReadAllText(Assert.Single(Directory.GetFiles(Path.Combine(home, "logs"))));
        Assert.Contains("exchange token acquisition 1: one token for every layer of the command", log, StringComparison.Ordinal);
    }

    [Fact]
    public void ATokenWithoutATenantTakesTheAccountsTenant()
    {
        var token = new TokenResult("token", DateTimeOffset.UtcNow.AddHours(1), ["Exchange.Manage"], null, FakeTokenBroker.Alice, TokenOrigin.Broker, null);

        Assert.Equal(FakeTokenBroker.Alice.TenantId, ExchangeConnection.ToExchangeToken(token).TenantId);
    }

    [Theory]
    [InlineData(ExchangeConnectWith.Organization, "contoso.onmicrosoft.com", null)]
    [InlineData(ExchangeConnectWith.UserPrincipalName, null, "alice@contoso.com")]
    [InlineData(ExchangeConnectWith.Both, "contoso.onmicrosoft.com", "alice@contoso.com")]
    public async Task TheTargetComesFromTheProfile(ExchangeConnectWith with, string? organization, string? account)
    {
        AuthService auth = Auth();

        ExchangeSessionOptions options = ExchangeConnection.Options(auth, null, new RequestLog(), with, _ => throw new InvalidOperationException("never started"));
        ExchangeSessionOptions shared = ExchangeConnection.Options(null, new RequestLog(), () => auth, with);

        Assert.Equal(new ExchangeTarget(organization, account), await options.Target(Ct));
        Assert.Equal(new ExchangeTarget(organization, account), await shared.Target(Ct));
    }

    [Fact]
    public async Task TheFactoriesOverASignInStartNothingUntilTheFirstCall()
    {
        AuthService auth = Auth();

        await using ExchangeSession session = ExchangeConnection.Create(null, new RequestLog(), () => auth);
        AdminApiClient? viaAuth = AdminApiConnection.Create(null, new RequestLog(), () => auth);
        AdminApiClient direct = AdminApiConnection.Create(auth, null, new RequestLog());
        AdminApiClient? shared = AdminApiConnection.Create(null, new RequestLog(), () => auth, ExchangeTokenSource.ForProfile(auth, null));

        Assert.Null(session.Hello);
        Assert.Equal(0, viaAuth!.Calls);
        Assert.Equal(0, direct.Calls);
        Assert.Equal(0, shared!.Calls);
    }

    [Fact]
    public async Task ATenantWithoutAnInitialDomainCannotBeConnected()
    {
        var transport = new ScriptedTransport(_ => new GraphResponse(200, [], """{"value":[{"verifiedDomains":[{"name":"contoso.com","isInitial":false}]},{}]}"""));

        AegisException exception = await Assert.ThrowsAsync<AegisException>(() =>
            ExchangeConnection.OrganizationAsync(new TenantProfile("work", "contoso.com", "00000000-0000-0000-0000-000000000010", "alice@contoso.com"), () => new GraphClient(transport), Ct));

        Assert.Equal(FailureKind.SidecarUnavailable, exception.Failure.Kind);
        Assert.StartsWith("Graph named no initial .onmicrosoft.com domain", exception.Failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AScriptThatIsMissingOrChangedNeverStarts()
    {
        string folder = ProfileStoreTests.TempHome();
        Directory.CreateDirectory(folder);
        string changed = Path.Combine(folder, SidecarScript.FileName);
        await File.WriteAllTextAsync(changed, "Write-Output 'not the build''s script'", Ct);

        AegisException missing = await Assert.ThrowsAsync<AegisException>(() => ExchangeConnection.StartAsync(Path.Combine(folder, "missing.ps1"), Ct));
        AegisException tampered = await Assert.ThrowsAsync<AegisException>(() => ExchangeConnection.StartAsync(changed, Ct));

        Assert.Equal(FailureKind.SidecarUnavailable, missing.Failure.Kind);
        Assert.StartsWith("the sidecar script cannot be used: ", missing.Failure.Message, StringComparison.Ordinal);
        Assert.Equal(FailureKind.SidecarUnavailable, tampered.Failure.Kind);
        Assert.StartsWith("the sidecar script cannot be used: ", tampered.Failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheBodyCarriesEveryKindOfValue()
    {
        string body = AdminApiClient.Body(new ExoCommand(AdminApiCmdlets.GetMailbox)
        {
            Parameters = new Dictionary<string, object?>
            {
                ["Nothing"] = null,
                ["Text"] = "finance",
                ["Switch"] = true,
                ["Small"] = 10,
                ["Large"] = 5_000_000_000L,
                ["List"] = List,
                ["Other"] = 2.5,
            },
        });

        Assert.Equal(
            """{"CmdletInput":{"CmdletName":"Get-Mailbox","Parameters":{"Nothing":null,"Text":"finance","Switch":true,"Small":10,"Large":5000000000,"List":["a","b"],"Other":"2.5"}}}""",
            body);
    }

    [Theory]
    [InlineData("", 0)]
    [InlineData("""[{"Name":"a"},{"Name":"b"}]""", 2)]
    [InlineData("5", 0)]
    [InlineData("""{"value":{"Name":"a"}}""", 1)]
    [InlineData("""{"value":"none"}""", 0)]
    [InlineData("""{"@odata.context":"x"}""", 0)]
    [InlineData("""{"Name":"a","@odata.context":"x"}""", 1)]
    public async Task EveryShapeOfAnAnswerIsRead(string body, int count)
    {
        AdminApiClient client = Admin(_ => new GraphResponse(200, [], body));

        ExoResult result = await client.InvokeAsync(new ExoCommand(AdminApiCmdlets.GetMailbox), Ct);

        Assert.Equal(count, result.Items.Count);
    }

    [Fact]
    public async Task AnAnswerThatIsNoJsonIsAServiceError()
    {
        AdminApiClient client = Admin(_ => new GraphResponse(200, [], "{not json"));

        AegisException exception = await Assert.ThrowsAsync<AegisException>(() => client.InvokeAsync(new ExoCommand(AdminApiCmdlets.GetMailbox), Ct));

        Assert.Equal(FailureKind.ServiceError, exception.Failure.Kind);
        Assert.StartsWith("the Admin API's answer to Get-Mailbox is not the JSON the client expected", exception.Failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AThrottledAnswerWithoutRetryAfterBacksOffExponentiallyUntilTheRetriesRunOut()
    {
        var delays = new List<TimeSpan>();
        AdminApiClient client = Admin(
            _ => new GraphResponse(503, [], ""),
            new GraphClientOptions { MaxRetries = 2, Jitter = () => 0, Delay = (delay, _) => { delays.Add(delay); return Task.CompletedTask; } });

        AegisException exception = await Assert.ThrowsAsync<AegisException>(() => client.InvokeAsync(new ExoCommand(AdminApiCmdlets.GetMailbox), Ct));

        Assert.Equal(FailureKind.Throttled, exception.Failure.Kind);
        Assert.Equal("the Exchange Online Admin API answered 503 to Get-Mailbox: HTTP 503", exception.Failure.Message);
        Assert.Equal([TimeSpan.FromSeconds(0.5), TimeSpan.FromSeconds(1)], delays);
        Assert.Equal(3, client.Calls);
    }

    [Fact]
    public async Task AThrottledAnswerBeyondTheBudgetIsNotRetried()
    {
        AdminApiClient client = Admin(
            _ => new GraphResponse(429, [new("Retry-After", "120")], ""),
            new GraphClientOptions { Jitter = () => 0, Delay = (_, _) => throw new InvalidOperationException("never waits") });

        AegisException exception = await Assert.ThrowsAsync<AegisException>(() => client.InvokeAsync(new ExoCommand(AdminApiCmdlets.GetMailbox), Ct));

        Assert.Equal(FailureKind.Throttled, exception.Failure.Kind);
        Assert.Equal(1, client.Calls);
    }

    [Fact]
    public void AFailureIsWrittenToTheVerboseLog()
    {
        string home = ProfileStoreTests.TempHome();
        using (LogSink.UseVerbose(LogSink.ForHome(home)))
        {
            AdminApiClient.Failure(new ExoCommand(AdminApiCmdlets.GetMailbox), new GraphResponse(500, [], """{"error":{"code":"Boom","message":"it broke"}}"""));
        }

        string log = File.ReadAllText(Assert.Single(Directory.GetFiles(Path.Combine(home, "logs"))));
        Assert.Contains("admin-api: Get-Mailbox answered 500: Boom: it broke", log, StringComparison.Ordinal);
        Assert.Equal(
            "the Exchange Online Admin API answered 500 to Get-Mailbox: Boom",
            AdminApiClient.Failure(new ExoCommand(AdminApiCmdlets.GetMailbox), new GraphResponse(500, [], """{"error":{"code":"Boom"}}""")).Failure.Message);
    }

    [Fact]
    public async Task TheTransportTurnsNoNetworkAndATimeoutIntoServiceErrors()
    {
        var shared = new HttpAdminApiTransport(_ => Task.FromResult("token"));
        var offline = new HttpAdminApiTransport(_ => Task.FromResult("token"), new ThrowingHandler(new HttpRequestException("no route to host")));
        var slow = new HttpAdminApiTransport(_ => Task.FromResult("token"), new ThrowingHandler(new TaskCanceledException("timed out")));
        var request = new GraphHttpRequest(HttpMethod.Post, new Uri(AdminApiClient.BaseUrl + "tenant/Mailbox"), new Dictionary<string, string>(), null);

        AegisException unreachable = await Assert.ThrowsAsync<AegisException>(() => offline.SendAsync(request, Ct));
        AegisException timedOut = await Assert.ThrowsAsync<AegisException>(() => slow.SendAsync(request, Ct));

        Assert.NotNull(shared);
        Assert.Equal(FailureKind.ServiceError, unreachable.Failure.Kind);
        Assert.Equal("the Exchange Online Admin API could not be reached: no route to host", unreachable.Failure.Message);
        Assert.Equal(FailureKind.ServiceError, timedOut.Failure.Kind);
        Assert.Equal("the Exchange Online Admin API did not answer within 100 s", timedOut.Failure.Message);
    }

    [Theory]
    [InlineData("{x", null, "(not JSON, 2 characters)")]
    [InlineData("", null, null)]
    [InlineData("[1]", null, "(root: array[1])")]
    [InlineData("\"text\"", null, "(root: string)")]
    [InlineData("""{"error":"flat"}""", null, "error: string")]
    [InlineData("""{"error":{}}""", null, "error: object")]
    [InlineData("""{"value":5}""", null, "value: number")]
    [InlineData("""{"value":[]}""", 0, "value: array[0]")]
    [InlineData("""{"value":[1,null,true]}""", 3, "value: array[3]")]
    [InlineData("""{"@odata.nextLink":5}""", null, "@odata.nextLink: number")]
    [InlineData("""{"@odata.nextLink":"relative"}""", null, "@odata.nextLink: string")]
    public void AnAnswerOfAnyShapeIsDescribedWithoutItsValues(string body, int? items, string? line)
    {
        AdminApiCall call = ExchangeProbes.Describe("Mailbox", "UPN:", 200, 5, body);

        Assert.Equal(items, call.Items);
        Assert.Null(call.NextLinkParameters);
        Assert.Null(call.ErrorCode);
        Assert.Null(call.ErrorMessage);
        if (line is null)
        {
            Assert.Empty(call.Shape);
        }
        else
        {
            Assert.Equal(line, call.Shape[0]);
        }
    }

    [Fact]
    public void AnItemsNestedListsAreDescribedByTheirFirstEntry()
    {
        AdminApiCall call = ExchangeProbes.Describe("Mailbox", "UPN:", 200, 5, """{"value":[{"Rights":[{"User":"x","Level":1}],"Sizes":[1,2],"None":[],"Flag":false}]}""");

        Assert.Contains("value[0].Rights[0]: object {User,Level}", call.Shape);
        Assert.Contains("value[0].Sizes[0]: number", call.Shape);
        Assert.Contains("value[0].Flag: boolean", call.Shape);
        Assert.DoesNotContain(call.Shape, shape => shape.StartsWith("value[0].None[0]", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AProbeWithoutNetworkListsEveryCallWithoutAStatus()
    {
        using var http = new HttpClient(new ThrowingHandler(new HttpRequestException("no route to host")));
        ExchangeToken token = SidecarFixtures.Token(DateTimeOffset.UtcNow.AddHours(1));

        AdminApiProbeReport organization = await ExchangeProbes.AdminApiAsync(http, token, Ct);
        AdminApiProbeReport mailbox = await ExchangeProbes.AdminApiAsync(http, token, new AdminApiProbeTargets("finance@contoso.com", Forwarding: true), Ct);

        Assert.False(organization.Answered);
        Assert.Equal(["UPN:", "AAD-UPN:", "UPN:"], organization.Calls.Select(call => call.AnchorForm));
        Assert.All(organization.Calls, call => Assert.Null(call.Status));
        Assert.Equal("no route to host", organization.Calls[0].ErrorMessage);
        Assert.Contains(mailbox.Calls, call => call.Endpoint.StartsWith("Mailbox (", StringComparison.Ordinal) && call.Status is null);
    }

    [Fact]
    public async Task AProbeKeepsTheAnchorFormThatWasAnswered()
    {
        using var http = new HttpClient(new AnchorHandler());

        AdminApiProbeReport report = await ExchangeProbes.AdminApiAsync(http, SidecarFixtures.Token(DateTimeOffset.UtcNow.AddHours(1)), Ct);

        Assert.True(report.Answered);
        Assert.Equal(["UPN:", "AAD-UPN:", "AAD-UPN:"], report.Calls.Select(call => call.AnchorForm));
        Assert.Equal([null, 200, 200], report.Calls.Select(call => call.Status));
    }

    [Fact]
    public async Task AMeasurementOfAHelloWithoutTheModulesFolderSaysNothingAboutOneDrive()
    {
        var session = new ExchangeSessionOptions
        {
            Start = _ => ScriptedSidecar.StartAsync([
                ["""{"aegis":1,"id":"{id}","type":"hello","data":{"pwsh":"7.6.0","module":"3.10.1"}}"""],
                ["""{"aegis":1,"id":"{id}","type":"connected","data":{"importMs":5,"connectMs":7,"missing":[]}}"""],
                ["""{"aegis":1,"id":"{id}","type":"invoked","data":{"items":[]}}"""]]),
            Token = _ => Task.FromResult(SidecarFixtures.Token(DateTimeOffset.UtcNow.AddHours(1))),
            Target = _ => Task.FromResult(new ExchangeTarget(SidecarFixtures.Organization, null)),
        };

        ConnectMeasurementReport report = await ConnectMeasurement.RunAsync(session, [ConnectVariant.SkipFormatData], runs: 1, Ct);

        Assert.Equal("3.10.1", report.Environment.Module);
        Assert.Null(report.Environment.ModuleUnderOneDrive);
        Assert.Null(report.Environment.FirstModulePathUnderOneDrive);
        Assert.Equal(0, report.Summary[0].Failed);
    }

    [Fact]
    public async Task AProbeNeedsATokenThatNamesItsTenant()
    {
        using var http = new HttpClient(new ThrowingHandler(new HttpRequestException("never sent")));
        ExchangeToken token = SidecarFixtures.Token(DateTimeOffset.UtcNow.AddHours(1)) with { TenantId = null };

        await Assert.ThrowsAsync<InvalidOperationException>(() => ExchangeProbes.AdminApiAsync(http, token, Ct));
    }

    [Theory]
    [InlineData("Exchange.Manage Exchange.ManageV2", true)]
    [InlineData("Exchange.ManageV2", false)]
    [InlineData("Exchange.Manage", false)]
    public void OneTokenMustCarryBothPermissions(string permissions, bool both) =>
        Assert.Equal(both, new ConnectProbeReport("organization", "7.6", "3.10", 1, 1, 1, 1, 1, 1, 60, permissions.Split(' ')).CarriesManageAndManageV2);

    [Fact]
    public void AnUnknownVariantIsAUsageError() =>
        Assert.Equal(FailureKind.Usage, Assert.Throws<AegisException>(() => ConnectMeasurement.ParseVariant("fastest")).Failure.Kind);

    [Fact]
    public async Task AMeasurementReadsTheModulePathAndCountsTheRunsThatFailed()
    {
        string modulePath = string.Join(Path.PathSeparator, "/modules/first", "/modules/second");
        string hello = $$$"""{"aegis":1,"id":"{id}","type":"hello","data":{"pwsh":"7.6.0","module":"3.10.1","moduleBase":"/modules/first/ExchangeOnlineManagement","modulePath":"{{{modulePath}}}"}}""";
        int starts = 0;
        var session = new ExchangeSessionOptions
        {
            Start = _ => ++starts switch
            {
                1 => ScriptedSidecar.StartAsync([[hello], ["""{"aegis":1,"id":"{id}","type":"connected","data":{"importMs":5,"connectMs":7}}"""], ["""{"aegis":1,"id":"{id}","type":"invoked","data":{"items":[]}}"""]]),
                2 => ScriptedSidecar.StartAsync([[hello], ["""{"aegis":1,"id":"{id}","type":"error","data":{"code":"connect-failed","message":"no such organization"}}"""]]),
                _ => throw new AegisException(FailureKind.SidecarUnavailable, "pwsh did not start", "run the doctor"),
            },
            Token = _ => Task.FromResult(SidecarFixtures.Token(DateTimeOffset.UtcNow.AddHours(1))),
            Target = _ => Task.FromResult(new ExchangeTarget(SidecarFixtures.Organization, null)),
        };

        ConnectMeasurementReport report = await ConnectMeasurement.RunAsync(session, [ConnectVariant.Default], runs: 3, Ct);

        Assert.Null(report.Runs[0].Error);
        Assert.Empty(report.Runs[0].Missing);
        Assert.Equal("sidecar-unavailable: the Exchange Online connect failed: no such organization", report.Runs[1].Error);
        Assert.True(report.Runs[1].StartMs > 0);
        Assert.Equal(0, report.Runs[2].StartMs);
        Assert.Equal(2, report.Summary[0].Failed);
        Assert.Equal("3.10.1", report.Environment.Module);
        Assert.False(report.Environment.ModuleUnderOneDrive);
        Assert.False(report.Environment.FirstModulePathUnderOneDrive);
        Assert.Equal(0, report.Environment.ModulePathEntriesUnderOneDrive);
    }

    [Fact]
    public async Task AMeasurementWhoseSidecarsNeverStartDescribesNoModule()
    {
        var session = new ExchangeSessionOptions
        {
            Start = _ => throw new AegisException(FailureKind.SidecarUnavailable, "pwsh did not start", "run the doctor"),
            Token = _ => Task.FromResult(SidecarFixtures.Token(DateTimeOffset.UtcNow.AddHours(1))),
            Target = _ => Task.FromResult(new ExchangeTarget(SidecarFixtures.Organization, null)),
        };

        ConnectMeasurementReport report = await ConnectMeasurement.RunAsync(session, [ConnectVariant.CommandName], runs: 1, Ct);

        Assert.Null(report.Environment.Pwsh);
        Assert.Null(report.Environment.Module);
        Assert.Null(report.Environment.ModuleUnderOneDrive);
        Assert.Null(report.Environment.FirstModulePathUnderOneDrive);
        Assert.Equal(1, report.Summary[0].Failed);
    }

    private static AdminApiClient Admin(Func<GraphHttpRequest, GraphResponse> answer, GraphClientOptions? options = null) =>
        new(new ScriptedTransport(answer), _ => Task.FromResult(new AdminApiTarget(AdminApiConnection.TestTenantId, AdminApiConnection.TestAccount)), null, options);

    private static AuthService Auth()
    {
        string home = ProfileStoreTests.TempHome();
        var auth = new AuthService(home, _ => new FakeTokenBroker());
        auth.Profiles.Save(new TenantProfile("default", "contoso.onmicrosoft.com", "00000000-0000-0000-0000-000000000010", "alice@contoso.com"));
        return auth;
    }

    private sealed class RecordingRunner : IExoCmdletRunner
    {
        public Task<ExoResult> InvokeAsync(ExoCommand command, CancellationToken cancellationToken) =>
            Task.FromResult(new ExoResult([], []));
    }

    /// <summary>No network for the <c>UPN:</c> anchor, an empty object for the <c>AAD-UPN:</c> one.</summary>
    private sealed class AnchorHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            request.Headers.GetValues("X-AnchorMailbox").Single().StartsWith("UPN:", StringComparison.Ordinal)
                ? Task.FromException<HttpResponseMessage>(new HttpRequestException("no route to host"))
                : Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("{}") });
    }

    private sealed class ThrowingHandler(Exception exception) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>(exception);
    }
}
