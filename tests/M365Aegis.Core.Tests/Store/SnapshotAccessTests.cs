using System.Diagnostics;
using M365Aegis.Core.Output;
using M365Aegis.Core.Snapshots;
using M365Aegis.Tests;

namespace M365Aegis.Core.Tests.Store;

/// <summary>
/// <c>access &lt;user&gt;</c> over the fixture snapshot (task 084): D-011's query path, a section a statement, the
/// counts of what reaches everyone, the snapshot's age, and the answers without a snapshot or without the user.
/// </summary>
public sealed class SnapshotAccessTests
{
    private static readonly DateTimeOffset Soon = SnapshotSamples.Taken.AddHours(2);

    private static async Task<string> HomeWithTheFixtureSnapshotAsync()
    {
        string home = SnapshotSamples.WriteProfile(PlanSamples.TempHome());
        await SnapshotSamples.SeedAsync(home);
        return home;
    }

    [Fact]
    public async Task DaveReachesThroughHisGroupsAndTheirNesting()
    {
        string home = await HomeWithTheFixtureSnapshotAsync();

        AccessReport report = SnapshotAccess.ForUser(home, "contoso", "dave@contoso.com", Soon);

        Assert.Equal(("2026-10-31T060000Z.sqlite", SnapshotSamples.Taken, 2.0), (report.Snapshot, report.Taken, report.AgeHours));
        Assert.Equal(new AccessUser("00000000-0000-0000-0000-000000000005", "Dave Example", "dave@contoso.com", "dave@contoso.com"), report.User);
        Assert.Equal(
            ["Finance Microsoft365 via Finance EU", "Finance EU Security direct", "Finance Staff MailEnabledSecurity via Finance EU", "Project Phoenix Microsoft365 direct"],
            report.Groups.Select(group => $"{group.DisplayName} {group.Kind} {(group.Direct ? "direct" : "via " + string.Join(", ", group.Via))}"));
        Assert.Equal(
            [new AccessSite("Finance", "https://contoso.sharepoint.com/sites/finance", "Finance", "member"), new AccessSite("Project Phoenix", "https://contoso.sharepoint.com/sites/phoenix", "Project Phoenix", "member")],
            report.Sites);
        Assert.Equal(
            [
                new AccessSharedItem("Finance", "Reports", "/", "direct", null, "read", null, "Finance EU"),
                new AccessSharedItem("Project Phoenix", "Roadmap.pptx", "/Plans", "link", "users", "read", null, "Finance Staff"),
                new AccessSharedItem("Project Phoenix", "Roadmap.pptx", "/Plans", "direct", null, "read", null, null),
            ],
            report.SharedItems);
        Assert.Equal([new AccessMailbox("Reception", "reception@contoso.com", "full-access", "Finance Staff")], report.Mailboxes);
        Assert.Equal([new AccessCalendar("Carol Example", "carol@contoso.com", "Calendar", "Reviewer", "Finance Staff")], report.Calendars);
        Assert.Equal(new AccessEveryone(1, 1, 1, 1), report.Everyone);
        Assert.Equal(2, report.Warnings.Count);
        // A warning counts, it names no site or file: those stay in `snapshot show`.
        Assert.Equal("sites is partial in this snapshot: 1 item(s) could not be read (forbidden 1); `m365-aegis snapshot show` names them", report.Warnings[0]);
        Assert.StartsWith("oneDrive was not read: other users' OneDrives", report.Warnings[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task AliceIsFoundByHerObjectIdOrHerAddressAndOwnsHerSite()
    {
        string home = await HomeWithTheFixtureSnapshotAsync();

        AccessReport byId = SnapshotAccess.ForUser(home, "contoso", "{00000000-0000-0000-0000-000000000001}", Soon);
        AccessReport byMail = SnapshotAccess.ForUser(home, "contoso", " ALICE@contoso.com ", Soon);

        Assert.Equal(byId.User, byMail.User);
        Assert.Equal(["Finance member", "Finance owner"], byId.Sites.Select(site => $"{site.Name} {site.Role}"));
        Assert.Equal([new AccessSharedItem("Finance", "Budget.xlsx", "/", "link", "users", "write", null, null)], byId.SharedItems);
        Assert.Equal(
            ["Finance full-access", "Finance send-as", "Reception full-access"],
            byId.Mailboxes.Select(mailbox => $"{mailbox.Mailbox} {mailbox.Right}"));
        Assert.All(byId.Mailboxes, mailbox => Assert.Null(mailbox.Through));
        Assert.Equal([new AccessCalendar("Finance", "finance@contoso.com", "Calendar", "Editor", null)], byId.Calendars);
    }

    [Fact]
    public async Task AnExternalAddressAnswersWithTheLinksThatNameIt()
    {
        string home = await HomeWithTheFixtureSnapshotAsync();

        AccessReport report = SnapshotAccess.ForUser(home, "contoso", "partner@fabrikam.com", Soon);

        Assert.Equal(new AccessUser(null, "Partner Example", null, "partner@fabrikam.com"), report.User);
        Assert.Empty(report.Groups);
        Assert.Single(report.SharedItems);
    }

    [Fact]
    public async Task AnOldSnapshotIsAnsweredWithAWarning()
    {
        string home = await HomeWithTheFixtureSnapshotAsync();

        AccessReport report = SnapshotAccess.ForUser(home, "contoso", "bob@contoso.com", SnapshotSamples.Taken.AddHours(40.4));

        Assert.Equal(40.4, report.AgeHours);
        Assert.Equal("the snapshot is 40.4 hours old, older than 36: run `m365-aegis snapshot take` for a fresh one", report.Warnings[0]);
    }

    [Fact]
    public async Task TheNewestReadableSnapshotAnswers()
    {
        string home = await HomeWithTheFixtureSnapshotAsync();
        string folder = SnapshotStore.Folder(home, "contoso");
        await File.WriteAllTextAsync(Path.Combine(folder, "2026-11-01T000000Z.sqlite"), "damaged", TestContext.Current.CancellationToken);

        Assert.Equal("2026-10-31T060000Z.sqlite", SnapshotAccess.ForUser(home, "contoso", "bob@contoso.com", Soon).Snapshot);
    }

    [Fact]
    public async Task NoSnapshotNoUserOrAnUnknownUserIsAnAnswerWithAHint()
    {
        string home = await HomeWithTheFixtureSnapshotAsync();

        AegisException none = Assert.Throws<AegisException>(() => SnapshotAccess.ForUser(PlanSamples.TempHome(), "contoso", "bob@contoso.com", Soon));
        AegisException empty = Assert.Throws<AegisException>(() => SnapshotAccess.ForUser(home, "contoso", "  ", Soon));
        AegisException unknown = Assert.Throws<AegisException>(() => SnapshotAccess.ForUser(home, "contoso", "nobody@contoso.com", Soon));

        Assert.Equal((FailureKind.NotFound, "no snapshot of the profile 'contoso' yet", SnapshotStore.TakeHint), (none.Failure.Kind, none.Message, none.Failure.Hint));
        Assert.Equal(FailureKind.Usage, empty.Failure.Kind);
        Assert.Equal((FailureKind.NotFound, "the snapshot 2026-10-31T060000Z.sqlite knows no user 'nobody@contoso.com'"), (unknown.Failure.Kind, unknown.Message));
    }

    [Fact]
    public async Task TheViewAnswersInMilliseconds()
    {
        string home = await HomeWithTheFixtureSnapshotAsync();
        SnapshotAccess.ForUser(home, "contoso", "dave@contoso.com", Soon);

        var clock = Stopwatch.StartNew();
        SnapshotAccess.ForUser(home, "contoso", "dave@contoso.com", Soon);

        // D-011's budget is a second in a cold process; warm, in process, the queries take a few milliseconds.
        Assert.True(clock.ElapsedMilliseconds < 1000, $"{clock.ElapsedMilliseconds} ms");
    }
}
