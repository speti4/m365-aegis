using M365Aegis.Core.Exchange;
using M365Aegis.Core.Exchange.Sidecar;
using M365Aegis.Core.Operations;
using M365Aegis.Core.Output;
using M365Aegis.Tests;

namespace M365Aegis.Core.Tests.Exchange;

/// <summary>
/// A FullAccess grant without automapping (task 059): the rows of the arguments and of the CSV file, the plan that
/// names the choice, and the apply that sends <c>-AutoMapping:$false</c> through the sidecar over the fake module,
/// with every other parameter still off the allow-list.
/// </summary>
public sealed class AutoMappingTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void NoAutomappingMarksTheFullAccessRowsOnly()
    {
        IReadOnlyList<AccessChangeRow> rows = MailboxLookups.AccessRows("reception", ["carol@contoso.com"], ["FullAccess,SendAs"], autoMapping: false);

        Assert.Equal([(MailboxRight.FullAccess, false), (MailboxRight.SendAs, true)], rows.Select(row => (row.Right, row.AutoMapping)));
        Assert.All(MailboxLookups.AccessRows("reception", ["carol@contoso.com"], ["FullAccess"]), row => Assert.True(row.AutoMapping));
    }

    [Fact]
    public void NoAutomappingWithoutFullAccessIsAUsageError()
    {
        AegisException exception = Assert.Throws<AegisException>(() => MailboxLookups.AccessRows("reception", ["carol@contoso.com"], ["SendAs", "SendOnBehalf"], autoMapping: false));

        Assert.Equal(FailureKind.Usage, exception.Failure.Kind);
        Assert.Equal("--no-automapping goes with --right FullAccess, not Send As or Send on Behalf", exception.Message);
    }

    [Fact]
    public void TheCsvsAutomappingColumnIsOptionalAndFalseOnAFullAccessLineOnly()
    {
        IReadOnlyList<AccessChangeRow> rows = MailboxLookups.AccessRowsFromCsv(PlanSamples.Csv("mailbox-grants-automapping.csv"));
        IReadOnlyList<AccessChangeRow> without = MailboxLookups.AccessRowsFromCsv(PlanSamples.Csv("mailbox-grants.csv"));

        Assert.Equal([(2, false), (3, true), (4, true)], rows.Select(row => (row.Row!.Value, row.AutoMapping)));
        Assert.All(without, row => Assert.True(row.AutoMapping));
    }

    [Theory]
    [InlineData("mailbox,user,right,automapping\nfinance,carol@contoso.com,SendAs,false\n", true, "automapping is false on a Send As line")]
    [InlineData("mailbox,user,right,automapping\nfinance,carol@contoso.com,FullAccess,maybe\n", true, "automapping is 'maybe'")]
    [InlineData("mailbox,user,right,automapping\nfinance,carol@contoso.com,FullAccess,false\n", false, "unknown column")]
    public void ACsvLineTheColumnDoesNotFitRefusesTheFile(string text, bool grant, string expected)
    {
        string path = Path.Combine(Path.GetTempPath(), $"m365-aegis-tests-{Guid.NewGuid():N}.csv");
        File.WriteAllText(path, text);
        try
        {
            AegisException exception = Assert.Throws<AegisException>(() => MailboxLookups.AccessRowsFromCsv(path, grant));

            Assert.Equal(FailureKind.Usage, exception.Failure.Kind);
            Assert.Contains(expected, exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ThePlanNamesTheChoiceAndTheApplySendsAutoMappingFalse()
    {
        await using ExchangeSession session = SidecarFixtures.Session();
        var recorder = new Recorder(session);
        await using var backends = new ExchangeBackends(recorder);

        WritePlan write = await MailboxLookups.PlanAccessAsync(
            backends, grant: true, MailboxLookups.AccessRows("finance", ["alice@contoso.com", "carol@contoso.com"], ["FullAccess"], autoMapping: false), Ct);
        CommandResult<Plan> applied = await PlanGuard.RunAsync(write, WriteMode.Apply, PlanSamples.Store(PlanSamples.TempHome()), Ct);

        PlanChange change = Assert.Single(write.Plan.Changes);
        Assert.Equal("FullAccess for Carol Example (carol@contoso.com), without automapping", change.Detail);
        Assert.Equal("holds FullAccess already; its automapping is left as it is", Assert.Single(write.Plan.Unchanged).State);
        Assert.True(applied.Ok);
        ExoCommand sent = Assert.Single(recorder.Commands, command => command.Cmdlet == ExoCmdlets.AddMailboxPermission);
        Assert.Equal(false, sent.Parameters["AutoMapping"]);
        Assert.Equal(["Identity", "User", "AccessRights", "InheritanceType", "AutoMapping", "Confirm"], sent.Parameters.Keys);
    }

    [Fact]
    public async Task AGrantWithAutomappingSendsNoAutoMappingParameter()
    {
        await using ExchangeSession session = SidecarFixtures.Session();
        var recorder = new Recorder(session);
        await using var backends = new ExchangeBackends(recorder);

        WritePlan write = await MailboxLookups.PlanAccessAsync(backends, grant: true, MailboxLookups.AccessRows("finance", ["carol@contoso.com"], ["FullAccess"]), Ct);
        await PlanGuard.RunAsync(write, WriteMode.Apply, PlanSamples.Store(PlanSamples.TempHome()), Ct);

        Assert.Equal("FullAccess for Carol Example (carol@contoso.com)", Assert.Single(write.Plan.Changes).Detail);
        ExoCommand sent = Assert.Single(recorder.Commands, command => command.Cmdlet == ExoCmdlets.AddMailboxPermission);
        Assert.False(sent.Parameters.ContainsKey("AutoMapping"));
    }

    [Fact]
    public void AutoMappingIsOnTheGrantsAllowListOnly()
    {
        var parameters = new Dictionary<string, object?> { ["Identity"] = "finance", ["User"] = "carol@contoso.com", ["AutoMapping"] = false };

        ExoCmdlets.EnsureAllowed(new ExoCommand(ExoCmdlets.AddMailboxPermission) { Parameters = parameters });
        AegisException revoke = Assert.Throws<AegisException>(() => ExoCmdlets.EnsureAllowed(new ExoCommand(ExoCmdlets.RemoveMailboxPermission) { Parameters = parameters }));
        AegisException other = Assert.Throws<AegisException>(() => ExoCmdlets.EnsureAllowed(
            new ExoCommand(ExoCmdlets.AddMailboxPermission) { Parameters = new Dictionary<string, object?> { ["Identity"] = "finance", ["Owner"] = "carol@contoso.com" } }));

        Assert.Equal(FailureKind.CmdletNotAllowed, revoke.Failure.Kind);
        Assert.Contains("-AutoMapping", revoke.Message, StringComparison.Ordinal);
        Assert.Contains("-Owner", other.Message, StringComparison.Ordinal);
    }

    /// <summary>A runner that passes every cmdlet on and keeps it, so a test sees what an apply sent.</summary>
    private sealed class Recorder(IExoCmdletRunner inner) : IExoCmdletRunner
    {
        internal List<ExoCommand> Commands { get; } = [];

        public Task<ExoResult> InvokeAsync(ExoCommand command, CancellationToken cancellationToken)
        {
            lock (Commands)
            {
                Commands.Add(command);
            }

            return inner.InvokeAsync(command, cancellationToken);
        }
    }
}
