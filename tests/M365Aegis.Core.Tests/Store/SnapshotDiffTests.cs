using System.Diagnostics;
using M365Aegis.Core.Output;
using M365Aegis.Core.Snapshots;
using M365Aegis.Tests;

namespace M365Aegis.Core.Tests.Store;

/// <summary>
/// <c>snapshot diff &lt;a&gt; &lt;b&gt;</c> over the fixture snapshot and its changed copy a day later (task 098): one
/// added and one removed row per domain, keyed on the principals (a renamed group and moved integer keys are no change),
/// the picks, a snapshot diffed with itself, and the domains a snapshot did not read.
/// </summary>
public sealed class SnapshotDiffTests
{
    private const string First = "2026-10-31T060000Z.sqlite";
    private const string Second = "2026-11-01T060000Z.sqlite";

    private static readonly DateTimeOffset Now = SnapshotSamples.ChangedTaken.AddHours(2);

    private static string SitesPartial(string snapshot) =>
        $"sites is partial in {snapshot} (1 item(s) not read): the sharing rows of those items may show as removed or added";

    private static async Task<string> HomeWithTwoSnapshotsAsync()
    {
        string home = SnapshotSamples.WriteProfile(PlanSamples.TempHome());
        await SnapshotSamples.SeedAsync(home);
        await SnapshotSamples.SeedChangedAsync(home);
        return home;
    }

    [Fact]
    public async Task EachDomainHasOneAddedAndOneRemovedRow()
    {
        string home = await HomeWithTwoSnapshotsAsync();

        SnapshotDiffReport diff = SnapshotDiff.Compare(home, "contoso", "yesterday", "latest", Now);

        Assert.Equal(new SnapshotDiffSide(First, SnapshotSamples.Taken, 26, "yesterday"), diff.From);
        Assert.Equal(new SnapshotDiffSide(Second, SnapshotSamples.ChangedTaken, 2, "latest"), diff.To);
        Assert.Equal(
            ["members True +1 -1", "owners True +1 -1", "sharing True +1 -1", "grants True +1 -1", "folderPermissions True +1 -1", "forwarding True +1 -1"],
            diff.Domains.Select(domain => $"{domain.Name} {domain.Compared} +{domain.Added} -{domain.Removed}"));
        Assert.Equal(
            [
                "members removed Finance EU | FIN-KIOSK-01 | member (device)",
                "members added Project Phoenix | bob@contoso.com | member",
                "owners removed Finance EU | carol@contoso.com | owner (graph)",
                "owners added Project Phoenix | dave@contoso.com | owner (graph)",
                "sharing removed Finance: /Budget.xlsx | everyone in the organization | link organization read",
                "sharing added Project Phoenix: /Plans/Roadmap.pptx | bob@contoso.com | link users read",
                "grants added finance@contoso.com | dave@contoso.com | full-access",
                "grants removed reception@contoso.com | Finance Staff | full-access",
                "folderPermissions removed carol@contoso.com: Calendar | Default | AvailabilityOnly",
                "folderPermissions added carol@contoso.com: Calendar | Default | Reviewer",
                "forwarding added alice@contoso.com | carol@contoso.com | recipient, keeps a copy",
                "forwarding removed bob@contoso.com | bob@fabrikam.com | smtp-address, external, keeps a copy",
            ],
            diff.Changes.Select(change => $"{change.Domain} {change.Change.ToString().ToLowerInvariant()} {change.What} | {change.Who} | {change.How}"));
        Assert.Equal((6, 6), (diff.Added, diff.Removed));
        // The ids: a group and its member by object id; a link to everyone has neither, a calendar's Default no principal.
        Assert.Equal(("00000000-0000-0000-0000-000000000032", "00000000-0000-0000-0000-000000000006"), (diff.Changes[0].WhatId, diff.Changes[0].WhoId));
        Assert.Equal((null, null), (diff.Changes[4].WhatId, diff.Changes[4].WhoId));
        Assert.Equal(("00000000-0000-0000-0000-000000000004", null), (diff.Changes[8].WhatId, diff.Changes[8].WhoId));
        Assert.Equal([SitesPartial(First), SitesPartial(Second)], diff.Warnings);
    }

    [Fact]
    public async Task TheEnvelopeNamesBothSnapshotsAndTheirAges()
    {
        string home = await HomeWithTwoSnapshotsAsync();

        SnapshotDiffReport diff = SnapshotDiff.Compare(home, "contoso", "2026-10-31", "2026-11-01T060000Z", Now);

        await Verify(EnvelopeJson.Serialize(CommandResult.Success(diff).ToEnvelope(new EnvelopeMeta([], 12, Backend.Local))), extension: "json");
    }

    [Fact]
    public async Task ASnapshotDiffedWithItselfIsEmpty()
    {
        string home = await HomeWithTwoSnapshotsAsync();

        SnapshotDiffReport latest = SnapshotDiff.Compare(home, "contoso", "LATEST", " 2026-11-01T060000Z.sqlite ", Now);
        SnapshotDiffReport first = SnapshotDiff.Compare(home, "contoso", First, "2026-10-31", Now);

        Assert.Equal((Second, Second), (latest.From.Snapshot, latest.To.Snapshot));
        Assert.Equal("2026-11-01T060000Z.sqlite", latest.To.Pick);
        Assert.Empty(latest.Changes);
        Assert.All(latest.Domains, domain => Assert.Equal((true, 0, 0), (domain.Compared, domain.Added, domain.Removed)));
        Assert.Equal([SitesPartial(Second)], latest.Warnings);
        Assert.Equal((First, First), (first.From.Snapshot, first.To.Snapshot));
        Assert.Empty(first.Changes);
    }

    [Fact]
    public async Task ADatePicksTheNewestReadableSnapshotOfThatDayOrBefore()
    {
        string home = await HomeWithTwoSnapshotsAsync();
        string folder = SnapshotStore.Folder(home, "contoso");
        await File.WriteAllTextAsync(Path.Combine(folder, "2026-10-31T235959Z.sqlite"), "damaged", TestContext.Current.CancellationToken);

        Assert.Equal(First, SnapshotDiff.Resolve(home, "contoso", "2026-10-31", Now).Detail.Snapshot.Name);
        Assert.Equal(First, SnapshotDiff.Resolve(home, "contoso", "Yesterday", Now).Detail.Snapshot.Name);
        Assert.Equal(Second, SnapshotDiff.Resolve(home, "contoso", "2026-12-24", Now).Detail.Snapshot.Name);
        Assert.Equal(Path.Combine(folder, Second), SnapshotDiff.Resolve(home, "contoso", "2026-11-01T060000Z", Now).File);
    }

    [Fact]
    public async Task AFirstSnapshotNewerThanTheSecondIsReadBackwardsWithAWarning()
    {
        string home = await HomeWithTwoSnapshotsAsync();

        SnapshotDiffReport diff = SnapshotDiff.Compare(home, "contoso", "latest", "yesterday", Now);

        Assert.Equal((6, 6), (diff.Added, diff.Removed));
        Assert.Equal("members added Finance EU", $"{diff.Changes[0].Domain} {diff.Changes[0].Change.ToString().ToLowerInvariant()} {diff.Changes[0].What}");
        Assert.Equal($"the first snapshot, {Second}, is newer than the second: what appeared since is listed as removed", diff.Warnings[0]);
    }

    [Fact]
    public async Task ADomainEitherSnapshotDidNotReadIsLeftOut()
    {
        string home = await HomeWithTwoSnapshotsAsync();
        string second = Path.Combine(SnapshotStore.Folder(home, "contoso"), Second);
        SnapshotSamples.Execute(second, """
            update manifest set value = replace(value, '"status":"ok"', '"status":"failed"') where key = 'domain.grants';
            update manifest set value = replace(value, '"status":"ok"', '"status":"skipped"') where key = 'domain.forwarding';
            delete from manifest where key in ('domain.members', 'order.members');
            """);

        SnapshotDiffReport diff = SnapshotDiff.Compare(home, "contoso", "yesterday", "latest", Now);

        Assert.Equal(
            ["members False", "owners True", "sharing True", "grants False", "folderPermissions True", "forwarding False"],
            diff.Domains.Select(domain => $"{domain.Name} {domain.Compared}"));
        Assert.Equal(["owners", "sharing", "folderPermissions"], diff.Changes.Select(change => change.Domain).Distinct());
        Assert.Contains($"members is left out: members was not read in {Second}", diff.Warnings);
        Assert.Contains($"grants is left out: grants was not read in {Second}", diff.Warnings);
        Assert.Contains($"forwarding is left out: forwarding was not read in {Second}", diff.Warnings);
    }

    [Fact]
    public async Task APickThatFindsNothingIsNotFoundAndOneThatIsNoneIsAUsageError()
    {
        string home = await HomeWithTwoSnapshotsAsync();

        AegisException none = Assert.Throws<AegisException>(() => SnapshotDiff.Compare(PlanSamples.TempHome(), "contoso", "latest", "latest", Now));
        AegisException early = Assert.Throws<AegisException>(() => SnapshotDiff.Compare(home, "contoso", "2026-01-01", "latest", Now));
        AegisException missing = Assert.Throws<AegisException>(() => SnapshotDiff.Compare(home, "contoso", "latest", "2026-01-01T000000Z", Now));
        AegisException word = Assert.Throws<AegisException>(() => SnapshotDiff.Compare(home, "contoso", "last-week", "latest", Now));

        Assert.Equal((FailureKind.NotFound, "no snapshot of the profile 'contoso' yet"), (none.Failure.Kind, none.Message));
        Assert.Equal((FailureKind.NotFound, "no snapshot of the profile 'contoso' taken on 2026-01-01 (UTC) or before"), (early.Failure.Kind, early.Message));
        Assert.Equal("`m365-aegis snapshot list` shows the snapshots by name", early.Failure.Hint);
        Assert.Equal((FailureKind.NotFound, "no readable snapshot '2026-01-01T000000Z.sqlite' of the profile 'contoso'"), (missing.Failure.Kind, missing.Message));
        Assert.Equal((FailureKind.Usage, "'last-week' names no snapshot"), (word.Failure.Kind, word.Message));
        Assert.StartsWith("give a snapshot's name as `m365-aegis snapshot list` shows it, `latest`, `yesterday` or a date", word.Failure.Hint, StringComparison.Ordinal);
        Assert.Throws<ArgumentNullException>(() => SnapshotDiff.Resolve(home, "contoso", null!, Now));
    }

    [Fact]
    public async Task TheDiffAnswersInMilliseconds()
    {
        string home = await HomeWithTwoSnapshotsAsync();
        SnapshotDiff.Compare(home, "contoso", "yesterday", "latest", Now);

        var clock = Stopwatch.StartNew();
        SnapshotDiff.Compare(home, "contoso", "yesterday", "latest", Now);

        // D-011's budget is a second in a cold process; warm, in process, the six statements take a few milliseconds.
        Assert.True(clock.ElapsedMilliseconds < 1000, $"{clock.ElapsedMilliseconds} ms");
    }
}
