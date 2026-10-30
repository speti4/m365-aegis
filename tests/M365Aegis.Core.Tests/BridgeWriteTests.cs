using System.Text.Json.Nodes;
using M365Aegis.Core.Auth;
using M365Aegis.Core.Bridge;
using M365Aegis.Core.Diagnostics;
using M365Aegis.Core.Operations;
using M365Aegis.Core.Output;
using M365Aegis.Core.Tests.Auth;

namespace M365Aegis.Core.Tests;

/// <summary>
/// The window's mailbox writes over the bridge (task 082): a write request is planned over the window's layers and
/// answered with the <c>plan</c> the command line's <c>--dry-run</c> prints, nothing sent and nothing recorded; an
/// <c>apply-plan</c> with the confirmation applies it once through the write guard over the window's warm session,
/// with the plan record the command line writes. The replies are held to the samples under tests/contracts/bridge,
/// which the front end's tests read too. Over the fake module, each test with a window of its own.
/// </summary>
public sealed class BridgeWriteTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AFullAccessGrantIsThePlanSampleAndItsConfirmedApplyThePlanAppliedSampleWithTheRecord()
    {
        await using var window = new BridgeWindow();

        BridgeMessage plan = await window.SendAsync(BridgeWindow.Sample(BridgeMessageType.MailboxAccessGrant));
        string planId = BridgeWindow.PlanId(plan);
        List<string> sentBeforeTheConfirmation = window.Sent();
        string[] recordsBeforeTheConfirmation = window.Records;
        BridgeMessage apply = BridgeWindow.Sample(BridgeMessageType.ApplyPlan);
        apply.Data!["planId"] = planId;
        BridgeMessage applied = await window.SendAsync(apply);

        AssertIsPlanSample(plan, planId);
        Assert.Matches("^[0-9a-f]{32}$", planId);
        Assert.Empty(sentBeforeTheConfirmation);
        Assert.Empty(recordsBeforeTheConfirmation);
        AssertIs(WithRecord(BridgeWindow.SampleNode(BridgeMessageType.PlanApplied), window.Home, applied), applied);
        // The write went over the window's one session: the plan read and the apply ran on one sidecar.
        Assert.Equal(["fake module {\"cmdlet\":\"Add-MailboxPermission\",\"parameters\":{\"AccessRights\":[\"FullAccess\"],\"AutoMapping\":false,\"Identity\":\"finance@contoso.com\",\"InheritanceType\":\"All\",\"User\":\"bob@contoso.com\"}}"], window.Sent());
        Assert.Equal(1, window.Exchange.Sessions);
        Assert.Equal(1, window.Exchange.Session!.Starts);

        // The record as the command line writes it, with the window as its source and the command line of the same plan.
        string record = Assert.Single(window.Records);
        PlanRecord written = PlanStore.Read(record)!;
        Assert.Equal(PlanStore.RecordVersion, written.Version);
        Assert.Equal("window", written.Source);
        Assert.Null(written.Profile);
        Assert.Equal(["mailbox", "access", "grant", "finance@contoso.com", "--user", "bob@contoso.com", "--right", "FullAccess", "--no-automapping", "--yes"], written.Arguments);
        Assert.Equal(PlanMode.Applied, written.Plan.Mode);
        Assert.Equal(ChangeStatus.Applied, Assert.Single(written.Plan.Changes).Result!.Status);

        // Applied once: the same id again is refused with nothing sent, and the grant is there for the next read.
        BridgeMessage again = await window.SendAsync(apply);
        BridgeMessage access = await window.SendAsync(BridgeMessageType.MailboxAccess, """{"mailbox":"finance@contoso.com"}""");
        AssertError(again, "usage", "no plan with this id waits for a confirmation in this window; nothing was sent");
        Assert.Single(window.Sent());
        Assert.Contains(access.Data!["holders"]!.AsArray(), holder => holder!["holder"]!.GetValue<string>() == "bob@contoso.com" && holder["right"]!.GetValue<string>() == "full-access");
    }

    [Fact]
    public async Task AForwardingClearRunsFromTheWindowWithAPlanAConfirmationAndARecord()
    {
        await using var window = new BridgeWindow();

        BridgeMessage plan = await window.SendAsync(BridgeWindow.Sample(BridgeMessageType.MailboxForwardingClear));
        BridgeMessage applied = await window.SendAsync(BridgeWindow.Apply(BridgeWindow.PlanId(plan), true));

        JsonNode change = Assert.Single(plan.Data!["plan"]!["changes"]!.AsArray())!;
        Assert.Equal(("clear", "Bob Example (bob@contoso.com)", "forwards to bob@fabrikam.com, keeps a copy", "no forwarding"), (Text(change, "action"), Text(change, "target"), Text(change, "current"), Text(change, "desired")));
        Assert.Equal("plan-applied", applied.Type);
        Assert.Null(applied.Data!["error"]);
        Assert.Equal("applied", Text(applied.Data["plan"]!, "mode"));
        Assert.Contains("\"cmdlet\":\"Set-Mailbox\"", Assert.Single(window.Sent()), StringComparison.Ordinal);
        PlanRecord written = PlanStore.Read(Assert.Single(window.Records))!;
        Assert.Equal(["mailbox", "forwarding", "clear", "bob@contoso.com", "--yes"], written.Arguments);
        Assert.EndsWith("-mailbox-forwarding-clear.json", Text(applied.Data["plan"]!, "record"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AForwardingSetAndARevokeArePlannedAsTheCommandLinePlansThem()
    {
        await using var window = new BridgeWindow();

        BridgeMessage set = await window.SendAsync(BridgeWindow.Sample(BridgeMessageType.MailboxForwardingSet));
        BridgeMessage revoke = await window.SendAsync(BridgeWindow.Sample(BridgeMessageType.MailboxAccessRevoke));
        BridgeMessage appliedSet = await window.SendAsync(BridgeWindow.Apply(BridgeWindow.PlanId(set), true));

        Assert.Equal("forwards to alice@fabrikam.com, keeps a copy", Text(set.Data!["plan"]!["changes"]![0]!, "desired"));
        Assert.Contains("is outside the accepted domains", set.Data["plan"]!["warnings"]![0]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Equal("Send As for Alice Example (alice@contoso.com)", Text(revoke.Data!["plan"]!["changes"]![0]!, "detail"));
        Assert.Equal("plan-applied", appliedSet.Type);
        PlanRecord written = PlanStore.Read(Assert.Single(window.Records))!;
        Assert.Equal(["mailbox", "forwarding", "set", "alice@contoso.com", "--to", "alice@fabrikam.com", "--keep-copy", "--yes"], written.Arguments);
    }

    [Fact]
    public async Task ARevokeKeepsTheRightsInTheOrderGivenAndLeavesAutomappingToTheGrant()
    {
        await using var window = new BridgeWindow();

        BridgeMessage plan = await window.SendAsync(
            BridgeMessageType.MailboxAccessRevoke,
            """{"mailbox":"reception","users":["bob@contoso.com"," "],"rights":["send-as","FullAccess","SendAs"],"noAutoMapping":true}""");
        BridgeMessage applied = await window.SendAsync(BridgeWindow.Apply(BridgeWindow.PlanId(plan), true));

        Assert.Equal(2, plan.Data!["plan"]!["changes"]!.AsArray().Count);
        Assert.Equal("plan-applied", applied.Type);
        PlanRecord written = PlanStore.Read(Assert.Single(window.Records))!;
        Assert.Equal(["mailbox", "access", "revoke", "reception", "--user", "bob@contoso.com", "--right", "SendAs", "--right", "FullAccess", "--yes"], written.Arguments);
    }

    [Fact]
    public async Task APlanWithNothingToChangeHasNoIdAndHoldsNothing()
    {
        await using var window = new BridgeWindow();

        BridgeMessage plan = await window.SendAsync(
            BridgeMessageType.MailboxAccessGrant,
            """{"mailbox":"finance@contoso.com","users":["alice@contoso.com"],"rights":["full-access"]}""");

        Assert.Equal("plan", plan.Type);
        Assert.Null(plan.Data!["planId"]);
        Assert.Equal("up-to-date", Text(plan.Data["plan"]!, "mode"));
        Assert.Equal("holds FullAccess already", Text(plan.Data["plan"]!["unchanged"]![0]!, "state"));
        Assert.Empty(window.Sent());
        Assert.Empty(window.Records);
    }

    [Fact]
    public async Task AnApplyWithoutTheConfirmationIsRefusedAndThePlanWaitsForIt()
    {
        await using var window = new BridgeWindow();
        string planId = BridgeWindow.PlanId(await window.SendAsync(BridgeWindow.Sample(BridgeMessageType.MailboxAccessGrant)));

        BridgeMessage refused = await window.SendAsync(BridgeWindow.Apply(planId, false));
        List<string> sentAfterTheRefusal = window.Sent();
        BridgeMessage applied = await window.SendAsync(BridgeWindow.Apply(planId, true));

        AssertError(refused, "refused-without-yes", "1 change(s) planned; nothing was sent");
        Assert.Equal(SignInHints.Window.Failures[FailureKind.RefusedWithoutYes], refused.Data!["hint"]!.GetValue<string>());
        Assert.Empty(sentAfterTheRefusal);
        Assert.Equal("plan-applied", applied.Type);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("""{}""")]
    [InlineData("""{"planId":"0123456789abcdef0123456789abcdef","confirm":true}""")]
    [InlineData("""{"planId":42,"confirm":true}""")]
    [InlineData("""{"planId":" ","confirm":true}""")]
    [InlineData("""{"planId":"0123456789abcdef0123456789abcdef","confirm":false}""")]
    public async Task AnApplyOfAPlanTheWindowDoesNotHoldIsAUsageError(string? data)
    {
        await using var window = new BridgeWindow();

        BridgeMessage reply = await window.SendAsync(BridgeMessageType.ApplyPlan, data);

        AssertError(reply, "usage", "no plan with this id waits for a confirmation in this window; nothing was sent");
        Assert.Equal("see the plan again on the card: a plan is applied once, and a sign-in or a sign-out drops it", reply.Data!["hint"]!.GetValue<string>());
        Assert.Null(window.Exchange.Session);
    }

    [Fact]
    public async Task APlanOlderThanItsLifetimeIsRefusedAndDropped()
    {
        await using var window = new BridgeWindow();
        string planId = BridgeWindow.PlanId(await window.SendAsync(BridgeWindow.Sample(BridgeMessageType.MailboxForwardingClear)));
        window.Clock.Advance(BridgeDispatcher.PlanLifetime + TimeSpan.FromSeconds(1));

        BridgeMessage stale = await window.SendAsync(BridgeWindow.Apply(planId, false));
        BridgeMessage again = await window.SendAsync(BridgeWindow.Apply(planId, true));

        AssertError(stale, "usage", "the plan is older than 15 minutes; nothing was sent");
        AssertError(again, "usage", "no plan with this id waits for a confirmation in this window; nothing was sent");
        Assert.Empty(window.Sent());
        Assert.Empty(window.Records);
    }

    [Fact]
    public async Task APlanAtItsLifetimeStillAppliesAndANewPlanDropsTheStaleOnes()
    {
        await using var window = new BridgeWindow();
        string first = BridgeWindow.PlanId(await window.SendAsync(BridgeWindow.Sample(BridgeMessageType.MailboxForwardingClear)));
        window.Clock.Advance(BridgeDispatcher.PlanLifetime);
        string second = BridgeWindow.PlanId(await window.SendAsync(BridgeWindow.Sample(BridgeMessageType.MailboxForwardingSet)));
        window.Clock.Advance(TimeSpan.FromSeconds(1));

        // The new plan dropped none: the first was not older than its lifetime then. A third one drops it now.
        string third = BridgeWindow.PlanId(await window.SendAsync(BridgeWindow.Sample(BridgeMessageType.MailboxAccessRevoke)));
        BridgeMessage firstApplied = await window.SendAsync(BridgeWindow.Apply(first, true));
        BridgeMessage secondApplied = await window.SendAsync(BridgeWindow.Apply(second, true));

        Assert.NotEqual(second, third);
        AssertError(firstApplied, "usage", "no plan with this id waits for a confirmation in this window; nothing was sent");
        Assert.Equal("plan-applied", secondApplied.Type);
    }

    [Fact]
    public async Task ASignOutAndASignInDropThePlansWaitingForAConfirmation()
    {
        await using var window = new BridgeWindow();
        window.Broker.Interactive = (scope, _) => FakeTokenBroker.Token(scope, FakeTokenBroker.Alice, TokenOrigin.Broker);
        window.Broker.Silent = (scope, account) => FakeTokenBroker.Token(scope, account, TokenOrigin.Broker);
        string beforeTheSignOut = BridgeWindow.PlanId(await window.SendAsync(BridgeWindow.Sample(BridgeMessageType.MailboxForwardingClear)));

        Assert.Equal("logged-out", (await window.SendAsync(BridgeWindow.Sample(BridgeMessageType.Logout))).Type);
        BridgeMessage afterTheSignOut = await window.SendAsync(BridgeWindow.Apply(beforeTheSignOut, true));
        string beforeTheSignIn = BridgeWindow.PlanId(await window.SendAsync(BridgeWindow.Sample(BridgeMessageType.MailboxForwardingClear)));
        Assert.Equal("signed-in", (await window.SendAsync(BridgeWindow.Sample(BridgeMessageType.Login))).Type);
        BridgeMessage afterTheSignIn = await window.SendAsync(BridgeWindow.Apply(beforeTheSignIn, true));

        AssertError(afterTheSignOut, "usage", "no plan with this id waits for a confirmation in this window; nothing was sent");
        AssertError(afterTheSignIn, "usage", "no plan with this id waits for a confirmation in this window; nothing was sent");
        Assert.Empty(window.Sent());
        Assert.Empty(window.Records);
    }

    [Theory]
    [InlineData(BridgeMessageType.MailboxAccessGrant, null, "no user was named")]
    [InlineData(BridgeMessageType.MailboxAccessGrant, """{"mailbox":"finance","users":"bob@contoso.com","rights":["full-access"]}""", "no user was named")]
    [InlineData(BridgeMessageType.MailboxAccessGrant, """{"mailbox":"finance","users":[42,null," "],"rights":["full-access"]}""", "no user was named")]
    [InlineData(BridgeMessageType.MailboxAccessRevoke, """{"mailbox":"finance","users":["bob@contoso.com"]}""", "no right was chosen")]
    [InlineData(BridgeMessageType.MailboxAccessGrant, """{"mailbox":"finance","users":["bob@contoso.com"],"rights":["send-as"],"noAutoMapping":true}""", "without automapping goes with FullAccess")]
    [InlineData(BridgeMessageType.MailboxAccessGrant, """{"mailbox":"finance","users":["bob@contoso.com"],"rights":["read"]}""", "unknown right 'read'")]
    [InlineData(BridgeMessageType.MailboxAccessGrant, """{"users":["bob@contoso.com"],"rights":["full-access"]}""", "the mailbox is missing")]
    [InlineData(BridgeMessageType.MailboxForwardingSet, """{"mailbox":"alice","keepCopy":true}""", "the forwarding address is missing")]
    [InlineData(BridgeMessageType.MailboxForwardingSet, """{"mailbox":"alice","to":"alice at fabrikam"}""", "the forwarding address: 'alice at fabrikam' is not an address")]
    [InlineData(BridgeMessageType.MailboxForwardingClear, """{"mailbox":" "}""", "the mailbox is missing")]
    public async Task AWriteRequestThatAsksForNothingWholeIsAUsageErrorAndStartsNothing(string type, string? data, string message)
    {
        await using var window = new BridgeWindow();

        BridgeMessage reply = await window.SendAsync(type, data);

        AssertError(reply, "usage", message);
        Assert.False(SignInHints.NamesACommand(reply.Data!["hint"]?.GetValue<string>()), reply.Data["hint"]?.GetValue<string>());
        Assert.Null(window.Exchange.Session);
        Assert.Empty(window.Records);
    }

    [Fact]
    public async Task AWriteRequestOfAMailboxThatIsNotThereIsNotFoundWithNothingHeld()
    {
        await using var window = new BridgeWindow();

        BridgeMessage reply = await window.SendAsync(BridgeMessageType.MailboxForwardingClear, """{"mailbox":"nobody"}""");

        AssertError(reply, "not-found", "mailbox 'nobody' not found");
        Assert.Empty(window.Sent());
    }

    [Fact]
    public async Task AWriteRequestWithoutAnExchangeSessionIsSidecarUnavailable()
    {
        await using var window = new BridgeWindow();
        var dispatcher = new BridgeDispatcher(window.Services with { Exchange = null });

        BridgeMessage? reply = await dispatcher.DispatchAsync(BridgeWindow.Sample(BridgeMessageType.MailboxAccessGrant), Ct);

        Assert.Equal("sidecar-unavailable", reply?.Data?["code"]?.GetValue<string>());
    }

    [Fact]
    public async Task AnApplyThatPartlyFailsIsThePlanWithEachResultAndThePartialErrorInTheWindowsWords()
    {
        await using var window = new BridgeWindow();
        // The mail contact is no security principal: the fake module refuses its Send As, as the service does.
        BridgeMessage plan = await window.SendAsync(
            BridgeMessageType.MailboxAccessGrant,
            """{"mailbox":"finance","users":["bob@contoso.com","dave@example.com"],"rights":["send-as"]}""");

        BridgeMessage applied = await window.SendAsync(BridgeWindow.Apply(BridgeWindow.PlanId(plan), true));

        Assert.Equal("plan-applied", applied.Type);
        JsonNode error = applied.Data!["error"]!;
        Assert.Equal("partial", Text(error, "code"));
        Assert.Equal("1 of 2 change(s) applied, 1 failed", Text(error, "message"));
        Assert.StartsWith("the data lists each change's result and the service's message; the plan record is ", Text(error, "hint"), StringComparison.Ordinal);
        JsonArray changes = applied.Data["plan"]!["changes"]!.AsArray();
        Assert.Equal(["applied", "failed"], changes.Select(change => Text(change!["result"]!, "status")));
        JsonNode failed = changes[1]!["result"]!["error"]!;
        Assert.Equal("cmdlet-failed", Text(failed, "code"));
        // The core's hint names `--verbose`; the window shows its own, the record keeps the core's.
        Assert.Equal(SignInHints.Window.Failures[FailureKind.CmdletFailed], Text(failed, "hint"));
        PlanRecord written = PlanStore.Read(Assert.Single(window.Records))!;
        Assert.True(SignInHints.NamesACommand(written.Plan.Changes[1].Result!.Error!.Hint));
    }

    [Fact]
    public async Task AnApplyThatFailsAsAWholeCarriesThatFailureInTheWindowsWords()
    {
        await using var window = new BridgeWindow();
        BridgeMessage plan = await window.SendAsync(
            BridgeMessageType.MailboxAccessGrant,
            """{"mailbox":"finance","users":["dave@example.com"],"rights":["send-as"]}""");

        BridgeMessage applied = await window.SendAsync(BridgeWindow.Apply(BridgeWindow.PlanId(plan), true));

        JsonNode error = applied.Data!["error"]!;
        Assert.Equal("cmdlet-failed", Text(error, "code"));
        Assert.Equal(SignInHints.Window.Failures[FailureKind.CmdletFailed], Text(error, "hint"));
        Assert.Contains("isn't a valid security principal", Text(error, "message"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ACancelledApplyThrowsLeavesItsRecordFinishedAndSaysSoInTheLog()
    {
        await using var window = new BridgeWindow();
        string planId = BridgeWindow.PlanId(await window.SendAsync(BridgeWindow.Sample(BridgeMessageType.MailboxForwardingClear)));
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        using (LogSink.UseVerbose(LogSink.ForHome(window.Home)))
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => window.Dispatcher.DispatchAsync(BridgeWindow.Apply(planId, true), cancelled.Token));
        }

        Assert.Empty(window.Sent());
        PlanRecord written = PlanStore.Read(Assert.Single(window.Records))!;
        Assert.Equal(ChangeStatus.Skipped, Assert.Single(written.Plan.Changes).Result!.Status);
        Assert.Contains("window: apply-plan answered in ", Log(window.Home), StringComparison.Ordinal);
        Assert.Contains("(the apply stopped); exchange session 1", Log(window.Home), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ThePlanAndTheApplyWriteAWindowLineWithCountsOnly()
    {
        await using var window = new BridgeWindow();

        using (LogSink.UseVerbose(LogSink.ForHome(window.Home)))
        {
            string planId = BridgeWindow.PlanId(await window.SendAsync(BridgeWindow.Sample(BridgeMessageType.MailboxForwardingSet)));
            await window.SendAsync(BridgeWindow.Apply(planId, true));
        }

        string[] lines = [.. Log(window.Home).Split('\n').Where(line => line.Contains("window: ", StringComparison.Ordinal))];
        Assert.Equal(2, lines.Length);
        Assert.Matches(@"window: mailbox-forwarding-set answered in \d+ ms through pwsh \(1 change\(s\) planned, 0 unchanged, 1 warning\(s\)\); exchange session 1, ", lines[0]);
        Assert.Matches(@"window: apply-plan answered in \d+ ms \(mailbox-forwarding-set: 1 applied, 0 failed, 0 skipped\); exchange session 1, ", lines[1]);
        Assert.All(lines, line => Assert.DoesNotContain("alice", line, StringComparison.OrdinalIgnoreCase));
        Assert.All(lines, line => Assert.DoesNotContain("fabrikam", line, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TheLogLinesDescribeAPlanAndAnApplyByCounts()
    {
        var plan = new Plan(
            "mailbox-access-grant",
            "Grant mailbox rights on Finance (finance@contoso.com)",
            [
                new PlanChange(PlanAction.Add, "Finance", "FullAccess for Bob", "no FullAccess", "holds FullAccess") { Result = new ChangeResult(ChangeStatus.Applied) },
                new PlanChange(PlanAction.Add, "Finance", "Send As for Bob", "no Send As", "holds Send As") { Result = new ChangeResult(ChangeStatus.Failed, new EnvelopeError("cmdlet-failed", "refused")) },
                new PlanChange(PlanAction.Add, "Finance", "Send on Behalf for Bob", "no Send on Behalf", "holds Send on Behalf") { Result = new ChangeResult(ChangeStatus.Skipped) },
            ],
            [new PlanUnchanged("Finance", "FullAccess for Alice", "holds FullAccess already")],
            ["Bob is a group"]);

        Assert.Equal("3 change(s) planned, 1 unchanged, 1 warning(s)", BridgeDispatcher.DescribePlan(plan));
        Assert.Equal("mailbox-access-grant: 1 applied, 1 failed, 1 skipped", BridgeDispatcher.DescribeApply(plan));
        Assert.Throws<ArgumentNullException>(() => BridgeDispatcher.DescribePlan(null!));
        Assert.Throws<ArgumentNullException>(() => BridgeDispatcher.DescribeApply(null!));
    }

    [Fact]
    public void TheWriteRequestsAreTheTypesTheProtocolMarks()
    {
        Assert.Superset(
            new HashSet<string>(StringComparer.Ordinal) { "mailbox-access-grant", "mailbox-access-revoke", "mailbox-forwarding-set", "mailbox-forwarding-clear" },
            new HashSet<string>(BridgeMessageType.Writes, StringComparer.Ordinal));
        Assert.DoesNotContain(BridgeMessageType.ApplyPlan, BridgeMessageType.Writes);
        Assert.DoesNotContain(BridgeMessageType.MailboxAccess, BridgeMessageType.Writes);
    }

    private static string Text(JsonNode node, string name) => node[name]!.GetValue<string>();

    private static string Log(string home) =>
        string.Join("\n", Directory.GetFiles(Path.Combine(home, LogSink.FolderName)).Select(File.ReadAllText));

    private static void AssertError(BridgeMessage reply, string code, string message)
    {
        Assert.Equal("error", reply.Type);
        Assert.Equal(code, reply.Data?["code"]?.GetValue<string>());
        Assert.Equal(message, reply.Data?["message"]?.GetValue<string>());
    }

    /// <summary>The <c>plan</c> reply is the plan sample, with the id the dispatcher made.</summary>
    private static void AssertIsPlanSample(BridgeMessage reply, string planId)
    {
        JsonNode expected = BridgeWindow.SampleNode(BridgeMessageType.Plan);
        expected["data"]!["planId"] = planId;
        AssertIs(expected, reply);
    }

    /// <summary>
    /// The sample with the reply's id, its record named under the sample's home (<c>%LOCALAPPDATA%\m365-aegis</c>) as the
    /// reply names it under the test's.
    /// </summary>
    private static JsonNode WithRecord(JsonNode expected, string home, BridgeMessage reply)
    {
        string record = reply.Data!["plan"]!["record"]!.GetValue<string>();
        Assert.StartsWith(home, record, StringComparison.Ordinal);
        reply.Data["plan"]!["record"] = (@"%LOCALAPPDATA%\m365-aegis" + record[home.Length..]).Replace('/', '\\');
        expected["id"] = reply.Id;
        return expected;
    }

    private static void AssertIs(JsonNode expected, BridgeMessage reply)
    {
        JsonNode actual = JsonNode.Parse(BridgeProtocol.Write(reply))!;
        Assert.True(JsonNode.DeepEquals(expected, actual), $"the sample differs from the reply:\n{actual.ToJsonString()}");
    }
}
