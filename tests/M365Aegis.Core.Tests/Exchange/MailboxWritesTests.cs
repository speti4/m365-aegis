using System.Text;
using System.Text.RegularExpressions;
using M365Aegis.Core.Exchange;
using M365Aegis.Core.Exchange.AdminApi;
using M365Aegis.Core.Exchange.Sidecar;
using M365Aegis.Core.Operations;
using M365Aegis.Core.Output;
using M365Aegis.Tests;

namespace M365Aegis.Core.Tests.Exchange;

/// <summary>
/// The mailbox writes (task 053) against the fake module: each test has a sidecar of its own, since an apply changes
/// the fake's data for the life of the process, and a read after the apply shows the change. The Admin API answers
/// from the recorded fixtures where a test reads through the layers.
/// </summary>
public sealed partial class MailboxWritesTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<CommandResult<Plan>> ApplyAsync(WritePlan write) =>
        await PlanGuard.RunAsync(write, WriteMode.Apply, PlanSamples.Store(PlanSamples.TempHome()), Ct);

    [Fact]
    public async Task AGrantHeldAlreadyIsUnchangedAndTheOtherIsAppliedThroughTheSidecar()
    {
        await using ExchangeSession session = SidecarFixtures.Session();
        await using var backends = new ExchangeBackends(session);

        WritePlan write = await MailboxLookups.PlanAccessAsync(backends, grant: true, MailboxLookups.AccessRows("finance", ["alice@contoso.com", "bob@contoso.com"], ["FullAccess"]), Ct);

        PlanChange change = Assert.Single(write.Plan.Changes);
        Assert.Equal(("Finance (finance@contoso.com)", "FullAccess for Bob Example (bob@contoso.com)", "no FullAccess", "holds FullAccess"), (change.Target, change.Detail, change.Current, change.Desired));
        Assert.Equal("holds FullAccess already", Assert.Single(write.Plan.Unchanged).State);
        Assert.Equal("mailbox-access-grant", write.Plan.Operation);

        CommandResult<Plan> applied = await ApplyAsync(write);
        MailboxAccess after = (await MailboxLookups.AccessAsync(session, "finance", Ct)).Data!;
        WritePlan again = await MailboxLookups.PlanAccessAsync(backends, grant: true, MailboxLookups.AccessRows("finance", ["bob@contoso.com"], ["FullAccess"]), Ct);

        Assert.True(applied.Ok);
        Assert.Contains(after.Holders, holder => holder.Right == MailboxRight.FullAccess && holder.Holder == "bob@contoso.com");
        Assert.Empty(again.Plan.Changes);
        Assert.Equal("pwsh", backends.Backend);
    }

    [Fact]
    public async Task SendOnBehalfIsGrantedAndRevokedThroughSetMailbox()
    {
        await using ExchangeSession session = SidecarFixtures.Session();
        await using var backends = new ExchangeBackends(session);

        CommandResult<Plan> granted = await ApplyAsync(await MailboxLookups.PlanAccessAsync(backends, grant: true, MailboxLookups.AccessRows("reception", ["carol@contoso.com"], ["send-on-behalf"]), Ct));
        MailboxAccess between = (await MailboxLookups.AccessAsync(session, "reception", Ct)).Data!;
        WritePlan revoke = await MailboxLookups.PlanAccessAsync(backends, grant: false, MailboxLookups.AccessRows("reception", ["carol@contoso.com"], ["SendOnBehalf"]), Ct);
        CommandResult<Plan> revoked = await ApplyAsync(revoke);
        MailboxAccess after = (await MailboxLookups.AccessAsync(session, "reception", Ct)).Data!;

        Assert.True(granted.Ok);
        Assert.Contains(between.Holders, holder => holder.Right == MailboxRight.SendOnBehalf && holder.Address == "carol@contoso.com");
        Assert.Equal(("holds Send on Behalf", "no Send on Behalf"), (revoke.Plan.Changes[0].Current, revoke.Plan.Changes[0].Desired));
        Assert.True(revoked.Ok);
        Assert.DoesNotContain(after.Holders, holder => holder.Right == MailboxRight.SendOnBehalf);
    }

    [Fact]
    public async Task SendAsIsRevokedAndARightNotHeldIsLeftAlone()
    {
        await using ExchangeSession session = SidecarFixtures.Session();
        await using var backends = new ExchangeBackends(session);

        WritePlan write = await MailboxLookups.PlanAccessAsync(backends, grant: false, MailboxLookups.AccessRows("finance", ["alice@contoso.com", "bob@contoso.com"], ["SendAs"]), Ct);
        CommandResult<Plan> result = await ApplyAsync(write);
        MailboxAccess after = (await MailboxLookups.AccessAsync(session, "finance", Ct)).Data!;

        Assert.Equal("Send As for Alice Example (alice@contoso.com)", Assert.Single(write.Plan.Changes).Detail);
        Assert.Equal("no Send As", Assert.Single(write.Plan.Unchanged).State);
        Assert.True(result.Ok);
        Assert.DoesNotContain(after.Holders, holder => holder.Right == MailboxRight.SendAs);
        Assert.Contains(after.Holders, holder => holder.Right == MailboxRight.FullAccess && holder.Holder == "alice@contoso.com");
    }

    [Fact]
    public async Task ThroughTheLayersTheDelegatesComeFromTheAdminApi()
    {
        await using ExchangeSession session = SidecarFixtures.Session();
        await using var backends = new ExchangeBackends(session, AdminApiConnection.Fixtures(GraphFixtures.Root));

        WritePlan write = await MailboxLookups.PlanAccessAsync(backends, grant: true, MailboxLookups.AccessRows("finance", ["bob@contoso.com"], ["SendOnBehalf"]), Ct);

        Assert.Empty(write.Plan.Changes);
        Assert.Equal("holds Send on Behalf already", Assert.Single(write.Plan.Unchanged).State);
        Assert.Equal("pwsh+admin-api", backends.Backend);
    }

    [Fact]
    public async Task AGrantToAGroupAndToAMailContactWarnAndTheContactsFails()
    {
        await using ExchangeSession session = SidecarFixtures.Session();
        await using var backends = new ExchangeBackends(session);
        IReadOnlyList<AccessChangeRow> rows =
        [
            .. MailboxLookups.AccessRows("finance", ["finance-staff@contoso.com"], ["FullAccess,SendAs"]),
            .. MailboxLookups.AccessRows("finance", ["dave@example.com"], ["SendAs"]),
        ];

        WritePlan write = await MailboxLookups.PlanAccessAsync(backends, grant: true, rows, Ct);
        CommandResult<Plan> result = await ApplyAsync(write);

        Assert.Equal(3, write.Plan.Changes.Count);
        Assert.Equal(
            [
                "Finance Staff (finance-staff@contoso.com) is a group: every member, the nested ones too, gets FullAccess and Send As on Finance; Outlook does not add the mailbox for them on its own (no automapping through a group)",
                "Dave Private (dave@example.com) is a MailContact: Exchange grants mailbox rights to mailboxes, mail users and mail-enabled security groups, so the service will likely refuse it",
            ],
            write.Plan.Warnings);
        Assert.Equal(ExitCode.Partial, result.ExitCode);
        Assert.Equal([ChangeStatus.Applied, ChangeStatus.Applied, ChangeStatus.Failed], result.Data!.Changes.Select(change => change.Result!.Status));
        Assert.Equal("cmdlet-failed", result.Data.Changes[2].Result!.Error!.Code);
    }

    [Theory]
    [InlineData("finance", "nobody@contoso.com", "recipient 'nobody@contoso.com' not found")]
    [InlineData("nobody", "bob@contoso.com", "mailbox 'nobody' not found")]
    public async Task AHolderOrAMailboxThatDoesNotExistFailsThePlan(string mailbox, string user, string message)
    {
        await using ExchangeSession session = SidecarFixtures.Session();
        await using var backends = new ExchangeBackends(session);

        AegisException exception = await Assert.ThrowsAsync<AegisException>(() =>
            MailboxLookups.PlanAccessAsync(backends, grant: true, MailboxLookups.AccessRows(mailbox, [user], ["FullAccess"]), Ct));

        Assert.Equal(FailureKind.NotFound, exception.Failure.Kind);
        Assert.Equal(message, exception.Failure.Message);
    }

    [Fact]
    public async Task TheForwardingPlanComparesTheTargetAndTheCopyAndWarnsOfAnExternalAddress()
    {
        await using ExchangeSession session = SidecarFixtures.Session();
        await using var backends = new ExchangeBackends(session);

        WritePlan same = await MailboxLookups.PlanForwardingAsync(backends, MailboxLookups.ForwardingRows("bob", "bob@fabrikam.com", keepCopy: true, set: true), Ct);
        WritePlan other = await MailboxLookups.PlanForwardingAsync(backends, MailboxLookups.ForwardingRows("bob", "smtp:carol@fabrikam.com", keepCopy: false, set: true), Ct);
        WritePlan inside = await MailboxLookups.PlanForwardingAsync(backends, MailboxLookups.ForwardingRows("alice", "carol@contoso.com", keepCopy: true, set: true), Ct);

        Assert.Empty(same.Plan.Changes);
        Assert.Equal("forwards to bob@fabrikam.com, keeps a copy already", Assert.Single(same.Plan.Unchanged).State);
        PlanChange change = Assert.Single(other.Plan.Changes);
        Assert.Equal((PlanAction.Set, "forwards to bob@fabrikam.com, keeps a copy", "forwards to carol@fabrikam.com, keeps no copy"), (change.Action, change.Current, change.Desired));
        Assert.Equal(2, other.Plan.Warnings.Count);
        Assert.StartsWith("carol@fabrikam.com is outside the accepted domains", other.Plan.Warnings[0], StringComparison.Ordinal);
        Assert.StartsWith("Bob Example keeps no copy", other.Plan.Warnings[1], StringComparison.Ordinal);
        Assert.Equal(("no forwarding", "forwards to carol@contoso.com, keeps a copy"), (inside.Plan.Changes[0].Current, inside.Plan.Changes[0].Desired));
        Assert.Empty(inside.Plan.Warnings);
    }

    [Fact]
    public async Task SettingReplacesARecipientTargetAndClearingStopsTheForwarding()
    {
        await using ExchangeSession session = SidecarFixtures.Session();
        await using var backends = new ExchangeBackends(session);

        WritePlan set = await MailboxLookups.PlanForwardingAsync(backends, MailboxLookups.ForwardingRows("carol", "carol@contoso.com", keepCopy: true, set: true), Ct);
        CommandResult<Plan> setResult = await ApplyAsync(set);
        WritePlan clear = await MailboxLookups.PlanForwardingAsync(backends, [.. MailboxLookups.ForwardingRows("dave", null, false, set: false), .. MailboxLookups.ForwardingRows("alice", null, false, set: false)], Ct);
        CommandResult<Plan> clearResult = await ApplyAsync(clear);
        MailboxForwarders after = (await MailboxLookups.ForwardersAsync(session, Ct)).Data!;

        Assert.StartsWith("Carol Example forwards to the recipient 'Finance' (ForwardingAddress)", Assert.Single(set.Plan.Warnings), StringComparison.Ordinal);
        Assert.True(setResult.Ok);
        Assert.Equal(("forwards to the recipient 'Dave Private', keeps no copy", "no forwarding"), (clear.Plan.Changes[0].Current, clear.Plan.Changes[0].Desired));
        Assert.Equal("no forwarding", Assert.Single(clear.Plan.Unchanged).State);
        Assert.True(clearResult.Ok);
        Assert.Equal(
            [("bob@contoso.com", "bob@fabrikam.com"), ("carol@contoso.com", "carol@contoso.com")],
            after.Forwarders.Select(forwarder => (forwarder.Mailbox.PrimarySmtpAddress, forwarder.TargetAddress!)).OrderBy(pair => pair.Item1));
    }

    [Fact]
    public async Task AMailboxNamedTwiceIsRefused()
    {
        await using ExchangeSession session = SidecarFixtures.Session();
        await using var backends = new ExchangeBackends(session);
        IReadOnlyList<ForwardingRow> rows = [new("bob", "a@fabrikam.com", false, 2), new("bob@contoso.com", "b@fabrikam.com", false, 3)];

        AegisException exception = await Assert.ThrowsAsync<AegisException>(() => MailboxLookups.PlanForwardingAsync(backends, rows, Ct));

        Assert.Equal(FailureKind.Usage, exception.Failure.Kind);
        Assert.Equal("bob@contoso.com is named twice (lines 2 and 3)", exception.Failure.Message);
    }

    [Fact]
    public async Task ACsvOfThreeGrantsIsOnePlanAndTheApplyGoesPastTheFailedLine()
    {
        await using ExchangeSession session = SidecarFixtures.Session();
        await using var backends = new ExchangeBackends(session);
        IReadOnlyList<AccessChangeRow> rows = MailboxLookups.AccessRowsFromCsv(PlanSamples.Csv("mailbox-grants.csv"));

        WritePlan write = await MailboxLookups.PlanAccessAsync(backends, grant: true, rows, Ct);
        CommandResult<Plan> result = await ApplyAsync(write);

        Assert.Equal([(2, MailboxRight.FullAccess), (3, MailboxRight.SendAs), (4, MailboxRight.SendAs)], rows.Select(row => (row.Row!.Value, row.Right)));
        Assert.Equal([2, 3, 4], write.Plan.Changes.Select(change => change.Row!.Value));
        Assert.Equal("Grant mailbox rights on 2 mailboxes", write.Plan.Title);
        Assert.Equal(ExitCode.Partial, result.ExitCode);
        Assert.Equal("2 of 3 change(s) applied, 1 failed", result.Failure!.Message);
        Assert.Equal([ChangeStatus.Applied, ChangeStatus.Failed, ChangeStatus.Applied], result.Data!.Changes.Select(change => change.Result!.Status));
    }

    [Fact]
    public void TheForwardingCsvReadsTheCopyColumnAndRefusesAnUnknownValue()
    {
        IReadOnlyList<ForwardingRow> rows = MailboxLookups.ForwardingRowsFromCsv(PlanSamples.Csv("mailbox-forwarding-set.csv"), set: true);

        Assert.Equal([("bob", "bob@fabrikam.com", true), ("alice", "alice@fabrikam.com", false)], rows.Select(row => (row.Mailbox, row.To!, row.KeepCopy)));
        string file = Path.Combine(Path.GetTempPath(), $"m365-aegis-tests-{Guid.NewGuid():N}.csv");
        File.WriteAllText(file, "mailbox,to,keepCopy\nbob,bob@fabrikam.com,maybe\n");
        Assert.Contains("keepCopy is 'maybe'", Assert.Throws<AegisException>(() => MailboxLookups.ForwardingRowsFromCsv(file, set: true)).Failure.Message, StringComparison.Ordinal);
        File.WriteAllText(file, "mailbox,to\nbob,not-an-address\n");
        Assert.Contains("'not-an-address' is not an address", Assert.Throws<AegisException>(() => MailboxLookups.ForwardingRowsFromCsv(file, set: true)).Failure.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("FullAccess", MailboxRight.FullAccess)]
    [InlineData("full-access", MailboxRight.FullAccess)]
    [InlineData("sendas", MailboxRight.SendAs)]
    [InlineData("Send-On-Behalf", MailboxRight.SendOnBehalf)]
    [InlineData("SendOnBehalfOf", MailboxRight.SendOnBehalf)]
    [InlineData("ReadPermission", null)]
    public void ARightIsReadInAnyCase(string text, MailboxRight? right) =>
        Assert.Equal(right, MailboxLookups.ParseRight(text));

    [Fact]
    public void TheArgumentsNeedAMailboxAUserAndAKnownRight()
    {
        Assert.Equal(4, MailboxLookups.AccessRows("finance", ["bob@contoso.com", "carol@contoso.com"], ["FullAccess,SendAs", "fullaccess"]).Count);
        Assert.Throws<AegisException>(() => MailboxLookups.AccessRows("finance", [], ["FullAccess"]));
        Assert.Throws<AegisException>(() => MailboxLookups.AccessRows("finance", ["bob@contoso.com"], []));
        Assert.Contains("unknown right 'Owner'", Assert.Throws<AegisException>(() => MailboxLookups.AccessRows("finance", ["bob@contoso.com"], ["Owner"])).Failure.Message, StringComparison.Ordinal);
        Assert.Throws<AegisException>(() => MailboxLookups.ForwardingRows("bob", "bob at fabrikam", false, set: true));
    }

    [Fact]
    public void TheScriptsWriteParametersAreTheCores()
    {
        string script = Encoding.UTF8.GetString(SidecarScript.ReadBuildCopy()!);
        Match block = WriteParametersBlock().Match(script);

        Assert.True(block.Success, "the script has no $script:WriteParameters = @{ … } block");
        Dictionary<string, List<string>> lists = WriteParameterLine().Matches(block.Groups[1].Value)
            .ToDictionary(match => match.Groups[1].Value, match => QuotedName().Matches(match.Groups[2].Value).Select(name => name.Groups[1].Value).ToList());
        Assert.Equal(ExoCmdlets.WriteParameters.Keys.Order(StringComparer.Ordinal), lists.Keys.Order(StringComparer.Ordinal));
        foreach ((string cmdlet, IReadOnlyList<string> parameters) in ExoCmdlets.WriteParameters)
        {
            Assert.Equal(parameters, lists[cmdlet]);
            Assert.True(ExoCmdlets.IsAllowed(cmdlet));
        }
    }

    [Fact]
    public async Task AWriteParameterOffTheListIsRefusedBeforeTheSidecarStarts()
    {
        await using ExchangeSession session = SidecarFixtures.Session();
        var command = new ExoCommand(ExoCmdlets.SetMailbox) { Parameters = new Dictionary<string, object?> { ["Identity"] = "alice@contoso.com", ["HiddenFromAddressListsEnabled"] = true } };

        AegisException exception = await Assert.ThrowsAsync<AegisException>(() => session.InvokeAsync(command, Ct));

        Assert.Equal(ExitCode.CmdletNotAllowed, exception.Failure.ExitCode);
        Assert.Contains("Set-Mailbox -HiddenFromAddressListsEnabled", exception.Failure.Message, StringComparison.Ordinal);
        Assert.Equal(0, session.Starts);
    }

    [GeneratedRegex(@"\$script:WriteParameters = @\{(.*?)\n\}", RegexOptions.Singleline)]
    private static partial Regex WriteParametersBlock();

    [GeneratedRegex(@"'([A-Za-z-]+)' = @\(([^)]*)\)")]
    private static partial Regex WriteParameterLine();

    [GeneratedRegex(@"'([A-Za-z-]+)'")]
    private static partial Regex QuotedName();
}
