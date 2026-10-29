using M365Aegis.Core.Exchange;
using M365Aegis.Core.Graph;
using M365Aegis.Core.Output;
using M365Aegis.Tests;

namespace M365Aegis.Core.Tests.Exchange;

/// <summary>The message trace: the window rules, the request, the answer from the recorded fixtures.</summary>
public sealed class MessageTraceLookupTests
{
    /// <summary>The clock of the tests: the fixtures' windows end here.</summary>
    internal static readonly DateTimeOffset Now = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);

    private static readonly CancellationToken None = CancellationToken.None;

    [Fact]
    public void TheDefaultWindowIsTheLastTwoDays()
    {
        (DateTimeOffset from, DateTimeOffset until, string filter) = MessageTraceLookup.Describe(new MessageTraceQuery("alice@contoso.com", null, null, null), Now.AddMilliseconds(400));

        Assert.Equal(Now.AddDays(-2), from);
        Assert.Equal(Now, until);
        Assert.Equal("receivedDateTime ge 2026-10-18T12:00:00Z and receivedDateTime le 2026-10-20T12:00:00Z and senderAddress eq 'alice@contoso.com'", filter);
    }

    [Fact]
    public void SenderAndRecipientGoTogether()
    {
        (_, _, string filter) = MessageTraceLookup.Describe(new MessageTraceQuery("alice@contoso.com", "o'brien@fabrikam.com", 1, Now.AddDays(-30)), Now);

        Assert.EndsWith("senderAddress eq 'alice@contoso.com' and recipientAddress eq 'o''brien@fabrikam.com'", filter, StringComparison.Ordinal);
        Assert.StartsWith("receivedDateTime ge 2026-09-19T12:00:00Z and receivedDateTime le 2026-09-20T12:00:00Z", filter, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, null, null, 0, "--sender, --recipient")]
    [InlineData("alice@contoso.com", null, 11, 0, "at most 10 days")]
    [InlineData("alice@contoso.com", null, 0, 0, "at least 1")]
    [InlineData("alice@contoso.com", null, 10, -85, "keeps 90 days")]
    [InlineData("alice@contoso.com", null, 2, 1, "in the future")]
    public void AWindowTheServiceWouldNotAnswerIsAUsageError(string? sender, string? recipient, int? days, int untilOffsetDays, string expected)
    {
        var query = new MessageTraceQuery(sender, recipient, days, untilOffsetDays == 0 ? null : Now.AddDays(untilOffsetDays));

        AegisException exception = Assert.Throws<AegisException>(() => MessageTraceLookup.Describe(query, Now));

        Assert.Equal(ExitCode.Usage, exception.Failure.ExitCode);
        Assert.Contains(expected, exception.Message, StringComparison.Ordinal);
        Assert.NotNull(exception.Failure.Hint);
    }

    [Fact]
    public async Task ATraceAnswersFromTheFixturesOldestFirst()
    {
        (GraphClient graph, FixtureGraphTransport transport) = GraphFixtures.Client();

        CommandResult<MessageTrace> result = await MessageTraceLookup.RunAsync(graph, new MessageTraceQuery("alice@contoso.com", null, null, null), Now, None);

        MessageTrace trace = Assert.IsType<MessageTrace>(result.Data);
        Assert.Equal(["delivered", "delivered", "failed"], trace.Messages.Select(message => message.Status));
        Assert.Equal(["bob@contoso.com", "carol@contoso.com", "dave@fabrikam.com"], trace.Messages.Select(message => message.Recipient));
        Assert.Equal("Quarterly report", trace.Messages[0].Subject);
        Assert.Contains("$top=5000", Assert.Single(transport.Requests).Uri.Query, StringComparison.Ordinal);
        Assert.Equal(["00000000-0000-0000-0000-0000000000d1"], graph.RequestLog.Ids);
    }

    [Fact]
    public async Task AMissingServicePrincipalIsForbiddenWithTheProvisioningHint()
    {
        (GraphClient graph, _) = GraphFixtures.Client();

        AegisException exception = await Assert.ThrowsAsync<AegisException>(() =>
            MessageTraceLookup.RunAsync(graph, new MessageTraceQuery(null, "bob@contoso.com", null, null), Now, None));

        Assert.Equal(FailureKind.Forbidden, exception.Failure.Kind);
        Assert.Contains("service principal", exception.Failure.Hint, StringComparison.Ordinal);
        Assert.Contains(MessageTraceLookup.Permission, exception.Failure.Hint, StringComparison.Ordinal);
    }
}
