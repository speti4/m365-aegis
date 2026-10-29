using System.Text;
using M365Aegis.Core.Exchange;
using M365Aegis.Core.Graph;
using M365Aegis.Core.Output;
using M365Aegis.Core.Tests.Graph;
using M365Aegis.Tests;

namespace M365Aegis.Core.Tests.Exchange;

/// <summary>The delivery steps of one message (task 034): by trace id or Message-ID, per recipient, from the fixtures.</summary>
public sealed class MessageTraceDetailsLookupTests
{
    private const string Report = "00000000-0000-0000-0000-000000000051";

    private static readonly CancellationToken None = CancellationToken.None;

    [Fact]
    public async Task ATraceIdListsTheStepsOfEveryRecipient()
    {
        (GraphClient graph, FixtureGraphTransport transport) = GraphFixtures.Client();

        CommandResult<MessageTraceDetails> result = await MessageTraceDetailsLookup.RunAsync(graph, new MessageTraceDetailsQuery(Report, null), None);

        MessageTraceDetails details = result.Data!;
        Assert.Equal("<00000000-0000-0000-0000-000000000050@contoso.com>", details.MessageId);
        Assert.Equal("Quarterly report", details.Subject);
        Assert.Equal(["bob@contoso.com", "carol@contoso.com"], details.Recipients.Select(recipient => recipient.Recipient));
        Assert.All(details.Recipients, recipient => Assert.Equal("delivered", recipient.Status));
        Assert.Equal(["Receive", "Submit", "Deliver"], details.Recipients[0].Events.Select(step => step.Event));
        Assert.Null(details.Recipients[0].Events[0].Action);
        // The rows by id, then the two recipients' steps.
        Assert.Equal(3, transport.Requests.Count);
        Assert.Equal(3, graph.RequestLog.Ids.Count);
    }

    [Theory]
    [InlineData("<00000000-0000-0000-0000-000000000052@contoso.com>")]
    [InlineData("00000000-0000-0000-0000-000000000052@contoso.com")]
    public async Task AMessageIdFindsTheTraceAndTheFailedStep(string messageId)
    {
        (GraphClient graph, FixtureGraphTransport transport) = GraphFixtures.Client();

        CommandResult<MessageTraceDetails> result = await MessageTraceDetailsLookup.RunAsync(graph, new MessageTraceDetailsQuery(messageId, null), None);

        MessageTraceRecipient dave = Assert.Single(result.Data!.Recipients);
        Assert.Equal("00000000-0000-0000-0000-000000000053", dave.TraceId);
        Assert.Equal("failed", dave.Status);
        Assert.Equal("Fail", dave.Events[^1].Event);
        Assert.Contains("RecipientNotFound", dave.Events[^1].Description, StringComparison.Ordinal);
        Assert.Equal(2, transport.Requests.Count);
    }

    [Fact]
    public async Task ATraceIdWithARecipientIsOneRequest()
    {
        (GraphClient graph, FixtureGraphTransport transport) = GraphFixtures.Client();

        CommandResult<MessageTraceDetails> result = await MessageTraceDetailsLookup.RunAsync(graph, new MessageTraceDetailsQuery(Report, "bob@contoso.com"), None);

        MessageTraceRecipient bob = Assert.Single(result.Data!.Recipients);
        Assert.Null(bob.Status);
        Assert.Equal(3, bob.Events.Count);
        Assert.Equal(
            "https://graph.microsoft.com/v1.0/admin/exchange/tracing/messageTraces/00000000-0000-0000-0000-000000000051/getDetailsByRecipient(recipientAddress='bob@contoso.com')",
            Uri.UnescapeDataString(Assert.Single(transport.Requests).Uri.AbsoluteUri));
    }

    [Fact]
    public void TheRecipientIsAnODataStringInThePath()
    {
        GraphRequest request = MessageTraceDetailsLookup.Request(Report, "o'brien@fabrikam.com");

        Assert.Equal(MessageTraceLookup.Permission, request.Permission);
        Assert.EndsWith("/getDetailsByRecipient(recipientAddress='o''brien@fabrikam.com')", Uri.UnescapeDataString(GraphClient.BuildUri(request).AbsoluteUri), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnUnknownTraceIdIsNotFoundWithTheWindowInTheHint()
    {
        (GraphClient graph, _) = GraphFixtures.Client();

        AegisException exception = await Assert.ThrowsAsync<AegisException>(() =>
            MessageTraceDetailsLookup.RunAsync(graph, new MessageTraceDetailsQuery("00000000-0000-0000-0000-000000000059", null), None));

        Assert.Equal(FailureKind.NotFound, exception.Failure.Kind);
        Assert.Contains("48 hours", exception.Failure.Message, StringComparison.Ordinal);
        Assert.Contains("--recipient", exception.Failure.Hint, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnEmptyMessageIsAUsageError()
    {
        (GraphClient graph, FixtureGraphTransport transport) = GraphFixtures.Client();

        AegisException exception = await Assert.ThrowsAsync<AegisException>(() => MessageTraceDetailsLookup.RunAsync(graph, new MessageTraceDetailsQuery(" ", null), None));

        Assert.Equal(FailureKind.Usage, exception.Failure.Kind);
        Assert.Empty(transport.Requests);
    }

    [Fact]
    public async Task AMessageToMoreThanTenRecipientsNeedsARecipient()
    {
        var rows = new StringBuilder("{\"value\":[");
        for (int number = 1; number <= 11; number++)
        {
            rows.Append(number == 1 ? "" : ",").Append("{\"id\":\"" + Report + "\",\"recipientAddress\":\"user" + number + "@contoso.com\",\"status\":\"delivered\"}");
        }

        string body = rows.Append("]}").ToString();
        var transport = new ScriptedTransport(_ => new GraphResponse(200, [], body));

        AegisException exception = await Assert.ThrowsAsync<AegisException>(() =>
            MessageTraceDetailsLookup.RunAsync(new GraphClient(transport), new MessageTraceDetailsQuery(Report, null), None));

        Assert.Equal(FailureKind.Usage, exception.Failure.Kind);
        Assert.Contains("11 recipients", exception.Failure.Message, StringComparison.Ordinal);
        Assert.Single(transport.Requests);
    }

    [Fact]
    public async Task AMissingServicePrincipalIsTheTracesSetupFailure()
    {
        var transport = new ScriptedTransport(_ => new GraphResponse(
            401, [], "{\"error\":{\"code\":\"UnknownError\",\"message\":\"Service principal-less authentication failed.\"}}"));

        AegisException exception = await Assert.ThrowsAsync<AegisException>(() =>
            MessageTraceDetailsLookup.RunAsync(new GraphClient(transport), new MessageTraceDetailsQuery(Report, "bob@contoso.com"), None));

        Assert.Equal(FailureKind.Forbidden, exception.Failure.Kind);
        Assert.Contains("service principal", exception.Failure.Hint, StringComparison.Ordinal);
    }
}
