using M365Aegis.Core.Diagnostics;
using M365Aegis.Core.Exchange;
using M365Aegis.Core.Exchange.Sidecar;
using M365Aegis.Core.Output;
using M365Aegis.Tests;

namespace M365Aegis.Core.Tests.Exchange;

/// <summary>
/// The calendars' names of every mailbox in one piped read (task 100): over the fake module, and over canned answers
/// for the ways a folder is matched to its mailbox and the reads that fail.
/// </summary>
public sealed class CalendarPathsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheFakeModuleNamesEveryMailboxsCalendarInTwoCalls()
    {
        await using ExchangeSession sidecar = SidecarFixtures.Session();
        await using var counted = new ExchangeBackends(sidecar);

        IReadOnlyDictionary<string, string> paths = await MailboxLookups.CalendarPathsAsync(counted.Sidecar, Ct);

        // Reception's calendar is named in its own language; Finance's own calendar below the default one is not it.
        Assert.Equal("Kalender", paths["reception@contoso.com"]);
        Assert.Equal("Calendar", paths["finance@contoso.com"]);
        Assert.Equal("Kalender", paths["00000000-0000-0000-0000-000000000016"]);
        Assert.Equal(12, paths.Count);
        Assert.Equal(2, counted.SidecarCalls);
    }

    [Fact]
    public async Task AFolderIsMatchedByItsIdentityWhenItCarriesNoMailboxGuid()
    {
        var exo = new CannedRunner(command => command.Cmdlet == ExoCmdlets.GetMailbox
            ? MailboxEdgeTests.Items(
                """{"Name":"anna","PrimarySmtpAddress":"anna@contoso.com","ExternalDirectoryObjectId":"00000000-0000-0000-0000-000000000001","ExchangeGuid":"00000000-0000-0000-0000-000000000041"}""",
                """{"Name":"twin","DisplayName":"Twin","PrimarySmtpAddress":"twin1@contoso.com"}""",
                """{"Name":"twin","DisplayName":"Twin","PrimarySmtpAddress":"twin2@contoso.com"}""",
                """{"Name":"ben","PrimarySmtpAddress":"ben@contoso.com"}""",
                """{"Name":"cara","PrimarySmtpAddress":"cara@contoso.com"}""")
            : MailboxEdgeTests.Items(
                """{"Identity":"anna\\Naptár","FolderPath":"/Naptár","FolderType":"Calendar"}""",
                """{"Identity":"twin\\Calendar","FolderPath":"/Calendar","FolderType":"Calendar"}""",
                """{"Identity":"nobody-knows","FolderPath":"/Calendar","FolderType":"Calendar"}""",
                """{"MailboxGuid":"00000000-0000-0000-0000-000000000099","FolderPath":"/Calendar","FolderType":"Calendar"}""",
                """{"Identity":"ben\\Holidays","FolderPath":"/Holidays","FolderType":"User Created"}""",
                """{"Identity":"cara\\Calendar","FolderPath":"/Calendar","FolderType":"Calendar"}"""));

        IReadOnlyDictionary<string, string> paths = await MailboxLookups.CalendarPathsAsync(exo, Ct);

        // The two mailboxes named twin cannot be told apart, the folders that name no known mailbox are left out, ben has
        // no calendar, and cara, without an object id, is known by her address alone.
        Assert.Equal(new Dictionary<string, string>
        {
            ["00000000-0000-0000-0000-000000000001"] = "Naptár",
            ["anna@contoso.com"] = "Naptár",
            ["cara@contoso.com"] = "Calendar",
        }, paths);
        ExoCommand piped = exo.Commands[1];
        Assert.Equal(["00000000-0000-0000-0000-000000000001", "twin1@contoso.com", "twin2@contoso.com", "ben@contoso.com", "cara@contoso.com"], piped.Input);
        Assert.Equal("Calendar", piped.Parameters["FolderScope"]);
    }

    [Fact]
    public async Task NoMailboxOrAFailedReadIsAnEmptyAnswerAndARefusedSignInStops()
    {
        var none = new CannedRunner(_ => new ExoResult([], []));
        var failing = new CannedRunner(command => command.Cmdlet == ExoCmdlets.GetMailbox
            ? MailboxEdgeTests.Items("""{"Name":"anna","PrimarySmtpAddress":"anna@contoso.com"}""")
            : throw new AegisException(FailureKind.Throttled, "busy"));
        var refused = new CannedRunner(_ => throw new AegisException(FailureKind.Auth, "no sign-in"));

        Assert.Empty(await MailboxLookups.CalendarPathsAsync(none, Ct));
        Assert.Single(none.Commands);
        Assert.Empty(await MailboxLookups.CalendarPathsAsync(failing, Ct));
        string home = PlanSamples.TempHome();
        using (LogSink.UseVerbose(LogSink.ForHome(home)))
        {
            Assert.Empty(await MailboxLookups.CalendarPathsAsync(failing, Ct));
        }

        Assert.Contains(
            "the calendars' names could not be read in one call (throttled); each mailbox's is read on its own",
            File.ReadAllText(Assert.Single(Directory.GetFiles(Path.Combine(home, LogSink.FolderName)))),
            StringComparison.Ordinal);
        Assert.Equal(FailureKind.Auth, (await Assert.ThrowsAsync<AegisException>(() => MailboxLookups.CalendarPathsAsync(refused, Ct))).Failure.Kind);
    }
}
