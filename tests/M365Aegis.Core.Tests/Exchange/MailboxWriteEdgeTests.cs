using M365Aegis.Core.Exchange;
using M365Aegis.Core.Exchange.Sidecar;
using M365Aegis.Core.Graph;
using M365Aegis.Core.Operations;
using M365Aegis.Core.Output;
using M365Aegis.Core.Tests.Config;
using M365Aegis.Core.Tests.Graph;

namespace M365Aegis.Core.Tests.Exchange;

/// <summary>
/// The mailbox writes' rarer inputs over answers a test writes itself (<see cref="CannedRunner"/>): the usage errors of
/// the arguments and the files, a holder or a mailbox that is missing with its line, a holder named twice, the warnings
/// of each kind of holder; and the message trace lookups' edges over a scripted Graph.
/// </summary>
public sealed class MailboxWriteEdgeTests
{
    private const string Shared = """{"DisplayName":"Shared","PrimarySmtpAddress":"shared@contoso.com"}""";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void TheArgumentsNeedAUserARightAndAnAddress()
    {
        Assert.Null(MailboxLookups.ParseRight(null));
        Assert.Equal("no --user was named", Usage(() => MailboxLookups.AccessRows("shared", null, ["FullAccess"])));
        Assert.Equal("no --right was named", Usage(() => MailboxLookups.AccessRows("shared", ["bob@contoso.com"], null)));
        Assert.Equal("--to: '' is not an address", Usage(() => MailboxLookups.ForwardingRows("shared", null, keepCopy: false, set: true)));
        Assert.Equal("bob@fabrikam.com", Assert.Single(MailboxLookups.ForwardingRows("shared", " smtp:bob@fabrikam.com ", keepCopy: false, set: true)).To);
    }

    [Theory]
    [InlineData("a@b@contoso.com")]
    [InlineData("@contoso.com")]
    [InlineData("bob@")]
    [InlineData("bob example@contoso.com")]
    [InlineData("nobody")]
    public void AForwardingTargetIsOneAddress(string to) =>
        Assert.Equal($"--to: '{to}' is not an address", Usage(() => MailboxLookups.ForwardingRows("shared", to, keepCopy: false, set: true)));

    [Fact]
    public void AFileWithAnUnknownRightIsAUsageErrorWithItsLine()
    {
        string path = Csv("grants.csv", "mailbox,user,right\nshared@contoso.com,bob@contoso.com,Frobnicate\n");

        Assert.Equal("grants.csv, line 2: unknown right 'Frobnicate'", Usage(() => MailboxLookups.AccessRowsFromCsv(path)));
    }

    [Fact]
    public void AClearingFileNeedsOnlyTheMailboxAndNamesEachMailboxOnce()
    {
        string clear = Csv("clear.csv", "mailbox\nshared@contoso.com\nother@contoso.com\n");
        string twice = Csv("twice.csv", "mailbox,to\nshared@contoso.com,bob@fabrikam.com\nSHARED@contoso.com,carol@fabrikam.com\n");

        IReadOnlyList<ForwardingRow> rows = MailboxLookups.ForwardingRowsFromCsv(clear, set: false);

        Assert.Equal([("shared@contoso.com", (string?)null, (int?)2), ("other@contoso.com", null, 3)], rows.Select(row => (row.Mailbox, row.To, row.Row)));
        Assert.Equal("twice.csv: SHARED@contoso.com is named twice (lines 2 and 3)", Usage(() => MailboxLookups.ForwardingRowsFromCsv(twice, set: true)));
    }

    [Fact]
    public async Task APlanOfNoRowsIsAUsageError()
    {
        await using var backends = new ExchangeBackends(new CannedRunner(_ => MailboxEdgeTests.Items()));

        AegisException access = await Assert.ThrowsAsync<AegisException>(() => MailboxLookups.PlanAccessAsync(backends, grant: true, [], Ct));
        AegisException forwarding = await Assert.ThrowsAsync<AegisException>(() => MailboxLookups.PlanForwardingAsync(backends, [], Ct));

        Assert.Equal((FailureKind.Usage, "no change was asked for"), (access.Failure.Kind, access.Failure.Message));
        Assert.Equal((FailureKind.Usage, "no change was asked for"), (forwarding.Failure.Kind, forwarding.Failure.Message));
    }

    [Fact]
    public async Task AMissingMailboxOrHolderOfAFileIsNotFoundWithItsLine()
    {
        await using var noMailbox = new ExchangeBackends(new CannedRunner(command => command.Cmdlet == ExoCmdlets.GetRecipient
            ? MailboxEdgeTests.Items("""{"DisplayName":"Bob","PrimarySmtpAddress":"bob@contoso.com"}""")
            : MailboxEdgeTests.Items()));
        await using var noHolder = new ExchangeBackends(new CannedRunner(_ => MailboxEdgeTests.Items()));
        await using var refusedHolder = new ExchangeBackends(new CannedRunner(_ => throw new AegisException(FailureKind.NotFound, "couldn't be found", "check")));

        AegisException mailbox = await Assert.ThrowsAsync<AegisException>(() =>
            MailboxLookups.PlanAccessAsync(noMailbox, grant: true, [new AccessChangeRow("gone", "bob@contoso.com", MailboxRight.SendAs, 2)], Ct));
        AegisException holder = await Assert.ThrowsAsync<AegisException>(() =>
            MailboxLookups.PlanAccessAsync(noHolder, grant: true, [new AccessChangeRow("shared", "ghost@contoso.com", MailboxRight.SendAs)], Ct));
        AegisException refused = await Assert.ThrowsAsync<AegisException>(() =>
            MailboxLookups.PlanAccessAsync(refusedHolder, grant: true, [new AccessChangeRow("shared", "ghost@contoso.com", MailboxRight.SendAs, 4)], Ct));
        AegisException forwarding = await Assert.ThrowsAsync<AegisException>(() =>
            MailboxLookups.PlanForwardingAsync(noHolder, [new ForwardingRow("gone", null, false, 3)], Ct));

        Assert.Equal("mailbox 'gone' not found (line 2)", mailbox.Failure.Message);
        Assert.Equal("recipient 'ghost@contoso.com' not found", holder.Failure.Message);
        Assert.Equal("recipient 'ghost@contoso.com' not found (line 4)", refused.Failure.Message);
        Assert.Equal("mailbox 'gone' not found (line 3)", forwarding.Failure.Message);
        Assert.All(new[] { mailbox, holder, refused, forwarding }, exception => Assert.Equal(FailureKind.NotFound, exception.Failure.Kind));
    }

    [Fact]
    public async Task AMailboxNamedTwiceInTheArgumentsIsAUsageErrorWithoutLines()
    {
        await using var backends = new ExchangeBackends(new CannedRunner(_ => MailboxEdgeTests.Items(Shared)));

        AegisException exception = await Assert.ThrowsAsync<AegisException>(() =>
            MailboxLookups.PlanForwardingAsync(backends, [new ForwardingRow("shared", null, false), new ForwardingRow("shared@contoso.com", null, false, 3)], Ct));

        Assert.Equal("shared@contoso.com is named twice", exception.Failure.Message);
    }

    [Fact]
    public async Task OneHolderNamedTwoWaysIsPlannedOnceAndAHolderWithoutAnAddressIsKeyedByItsId()
    {
        var sidecar = new CannedRunner(command => command.Cmdlet switch
        {
            ExoCmdlets.GetRecipient => (string)command.Parameters["Identity"]! switch
            {
                "bob" or "bob@contoso.com" => MailboxEdgeTests.Items("""{"DisplayName":"Bob Example","PrimarySmtpAddress":"bob@contoso.com"}"""),
                "group-id" => MailboxEdgeTests.Items("""{"Name":"Sales Team","ExternalDirectoryObjectId":"00000000-0000-0000-0000-000000000081","RecipientTypeDetails":"MailUniversalSecurityGroup"}"""),
                _ => MailboxEdgeTests.Items("{}"),
            },
            ExoCmdlets.GetMailbox => MailboxEdgeTests.Items(Shared),
            _ => MailboxEdgeTests.Items(),
        });
        await using var backends = new ExchangeBackends(sidecar);

        WritePlan write = await MailboxLookups.PlanAccessAsync(
            backends,
            grant: true,
            [
                new AccessChangeRow("shared", "bob", MailboxRight.SendAs),
                new AccessChangeRow("shared", "bob@contoso.com", MailboxRight.SendAs),
                new AccessChangeRow("shared", "group-id", MailboxRight.SendAs),
                new AccessChangeRow("shared", "nameless", MailboxRight.SendAs),
            ],
            Ct);

        Assert.Equal(["Send As for Bob Example (bob@contoso.com)", "Send As for Sales Team", "Send As for nameless"], write.Plan.Changes.Select(change => change.Detail));
        PlanUnchanged twice = Assert.Single(write.Plan.Unchanged);
        Assert.Equal("named twice: the change is planned once", twice.State);
        Assert.Contains("Sales Team is a group: every member, the nested ones too, gets Send As on Shared", write.Plan.Warnings);
    }

    [Fact]
    public async Task ARevokeOfFullAccessRemovesTheHeldRight()
    {
        var sidecar = new CannedRunner(command => command.Cmdlet switch
        {
            ExoCmdlets.GetRecipient => MailboxEdgeTests.Items("""{"DisplayName":"bob@contoso.com","PrimarySmtpAddress":"bob@contoso.com"}"""),
            ExoCmdlets.GetMailbox => MailboxEdgeTests.Items(Shared),
            ExoCmdlets.GetMailboxPermission => MailboxEdgeTests.Items("""{"User":"bob@contoso.com","AccessRights":["FullAccess"]}"""),
            _ => MailboxEdgeTests.Items(),
        });
        await using var backends = new ExchangeBackends(sidecar);

        WritePlan write = await MailboxLookups.PlanAccessAsync(backends, grant: false, [new AccessChangeRow("shared", "bob@contoso.com", MailboxRight.FullAccess)], Ct);

        PlanChange change = Assert.Single(write.Plan.Changes);
        Assert.Equal((PlanAction.Remove, "FullAccess for bob@contoso.com"), (change.Action, change.Detail));
        Assert.Empty(write.Plan.Warnings);
    }

    [Theory]
    [InlineData("MailContact", "is a MailContact")]
    [InlineData("MailUniversalDistributionGroup", "is a MailUniversalDistributionGroup")]
    [InlineData("DynamicDistributionGroup", "is a DynamicDistributionGroup")]
    [InlineData("MailNonUniversalDistributionGroupX", "is a MailNonUniversalDistributionGroupX")]
    [InlineData("UserMailbox", null)]
    [InlineData(null, null)]
    public async Task AHolderThatCannotHoldARightIsWarnedAbout(string? type, string? warning)
    {
        string holder = type is null ? """{"Name":"Carol"}""" : $$"""{"Name":"Carol","RecipientTypeDetails":"{{type}}"}""";
        var sidecar = new CannedRunner(command => command.Cmdlet switch
        {
            ExoCmdlets.GetRecipient => MailboxEdgeTests.Items(holder),
            ExoCmdlets.GetMailbox => MailboxEdgeTests.Items(Shared),
            _ => MailboxEdgeTests.Items(),
        });
        await using var backends = new ExchangeBackends(sidecar);

        WritePlan write = await MailboxLookups.PlanAccessAsync(backends, grant: true, [new AccessChangeRow("shared", "carol", MailboxRight.SendOnBehalf)], Ct);

        if (warning is null)
        {
            Assert.Empty(write.Plan.Warnings);
        }
        else
        {
            Assert.Contains(write.Plan.Warnings, line => line.StartsWith($"Carol {warning}", StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task AGroupGrantedSeveralRightsIsWarnedAboutThemAllAndTheAutomapping()
    {
        var sidecar = new CannedRunner(command => command.Cmdlet switch
        {
            ExoCmdlets.GetRecipient => MailboxEdgeTests.Items("""{"Name":"Sales","RecipientTypeDetails":"MailUniversalSecurityGroup"}"""),
            ExoCmdlets.GetMailbox => MailboxEdgeTests.Items(Shared),
            _ => MailboxEdgeTests.Items(),
        });
        await using var backends = new ExchangeBackends(sidecar);

        WritePlan write = await MailboxLookups.PlanAccessAsync(backends, grant: true, MailboxLookups.AccessRows("shared", ["sales"], ["FullAccess,SendAs,SendOnBehalf"]), Ct);

        Assert.Equal(
            "Sales is a group: every member, the nested ones too, gets FullAccess, Send As and Send on Behalf on Shared; Outlook does not add the mailbox for them on its own (no automapping through a group)",
            Assert.Single(write.Plan.Warnings));
    }

    [Fact]
    public async Task AMessageMustBeNamed()
    {
        var graph = new GraphClient(new ScriptedTransport(_ => new GraphResponse(200, [], """{"value":[]}""")));

        AegisException exception = await Assert.ThrowsAsync<AegisException>(() => MessageTraceDetailsLookup.RunAsync(graph, new MessageTraceDetailsQuery(null!, null), Ct));

        Assert.Equal(FailureKind.Usage, exception.Failure.Kind);
    }

    [Theory]
    [InlineData("00000000-0000-0000-0000-000000000051", null, "no message with the trace id 00000000-0000-0000-0000-000000000051 in the trace's last 48 hours")]
    [InlineData("<abc@contoso.com>", "bob@contoso.com", "no message with the Message-ID <abc@contoso.com> to bob@contoso.com in the trace's last 48 hours")]
    public async Task AMessageTheTraceDoesNotHoldIsNotFound(string message, string? recipient, string expected)
    {
        var graph = new GraphClient(new ScriptedTransport(_ => new GraphResponse(200, [], """{"value":[]}""")));

        AegisException exception = await Assert.ThrowsAsync<AegisException>(() => MessageTraceDetailsLookup.RunAsync(graph, new MessageTraceDetailsQuery(message, recipient), Ct));

        Assert.Equal((FailureKind.NotFound, expected), (exception.Failure.Kind, exception.Failure.Message));
    }

    [Theory]
    [InlineData("<abc@contoso.com>", "<abc@contoso.com>")]
    [InlineData("<abc@contoso.com", "<abc@contoso.com>")]
    [InlineData("abc@contoso.com>", "<abc@contoso.com>")]
    [InlineData("abc@contoso.com", "<abc@contoso.com>")]
    public void AMessageIdIsKeptInAngleBrackets(string given, string kept) =>
        Assert.Equal(kept, MessageTraceDetailsLookup.MessageId(given));

    [Fact]
    public void OnlyAForbiddenAnswerOrAMissingServicePrincipalIsASetupFailure()
    {
        Assert.True(MessageTraceLookup.IsSetupFailure(new AegisException(FailureKind.Forbidden, "denied", "consent")));
        Assert.True(MessageTraceLookup.IsSetupFailure(new AegisException(FailureKind.Auth, "the service principal is missing", "provision it")));
        Assert.False(MessageTraceLookup.IsSetupFailure(new AegisException(FailureKind.Auth, "the token expired", "login")));
        Assert.False(MessageTraceLookup.IsSetupFailure(new AegisException(FailureKind.NotFound, "no such message", "check")));
    }

    private static string Usage(Func<object> action)
    {
        AegisException exception = Assert.Throws<AegisException>(action);
        Assert.Equal(FailureKind.Usage, exception.Failure.Kind);
        return exception.Failure.Message;
    }

    private static string Csv(string name, string text)
    {
        string folder = ProfileStoreTests.TempHome();
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, name);
        File.WriteAllText(path, text);
        return path;
    }
}
