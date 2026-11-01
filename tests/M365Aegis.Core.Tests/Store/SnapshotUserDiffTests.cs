using M365Aegis.Core.Output;
using M365Aegis.Core.Snapshots;
using M365Aegis.Tests;

namespace M365Aegis.Core.Tests.Store;

/// <summary>
/// <c>snapshot diff --user</c> over the fixture snapshot and its changed copy a day later (task 114): one user's gained
/// and lost access in the access view's sections, the nested membership bob gains through the new group Audit and what
/// comes through it, the user found in both snapshots by identity, and the window's pick of the snapshot before the
/// newest (task 115).
/// </summary>
public sealed class SnapshotUserDiffTests
{
    private const string First = "2026-10-31T060000Z.sqlite";
    private const string Second = "2026-11-01T060000Z.sqlite";

    private static readonly DateTimeOffset Now = SnapshotSamples.ChangedTaken.AddHours(2);

    private static async Task<string> HomeWithTwoSnapshotsAsync()
    {
        string home = SnapshotSamples.WriteProfile(PlanSamples.TempHome());
        await SnapshotSamples.SeedAsync(home);
        await SnapshotSamples.SeedChangedAsync(home);
        return home;
    }

    private static string Second_(string home) => Path.Combine(SnapshotStore.Folder(home, "contoso"), Second);

    private static IEnumerable<string> Lines(SnapshotDiffReport diff) =>
        diff.Changes.Select(change =>
            $"{change.Domain} {change.Change.ToString().ToLowerInvariant()} {change.What} | {change.Who} | {change.How} | {change.Through ?? "direct"}");

    [Fact]
    public async Task BobGainsFinanceStaffThroughTheNewGroupAuditAndWhatComesThroughIt()
    {
        string home = await HomeWithTwoSnapshotsAsync();

        SnapshotDiffReport diff = SnapshotDiff.Compare(home, "contoso", "yesterday", "latest", Now, " bob@contoso.com ");

        Assert.Equal(new AccessUser("00000000-0000-0000-0000-000000000003", "Bob Example", "bob@contoso.com", "bob@contoso.com"), diff.User);
        Assert.Equal(
            ["groups +3 -0", "owners +0 -0", "sites +1 -0", "sharedItems +2 -0", "mailboxes +0 -0", "calendars +1 -0", "forwarding +0 -1"],
            diff.Domains.Select(domain => $"{domain.Name} +{domain.Added} -{domain.Removed}"));
        Assert.Equal(
            [
                "groups added Audit | bob@contoso.com | member | direct",
                "groups added Finance Staff | bob@contoso.com | member | Audit",
                "groups added Project Phoenix | bob@contoso.com | member | direct",
                "sites added Project Phoenix | bob@contoso.com | member | Project Phoenix",
                "sharedItems added Project Phoenix: /Plans/Roadmap.pptx | bob@contoso.com | link users read | direct",
                "sharedItems added Project Phoenix: /Plans/Roadmap.pptx | Finance Staff | link users read | Finance Staff",
                "calendars added carol@contoso.com: Calendar | Finance Staff | Reviewer | Finance Staff",
                "forwarding removed bob@contoso.com | bob@fabrikam.com | smtp-address, external, keeps a copy | direct",
            ],
            Lines(diff));
        Assert.Equal(("00000000-0000-0000-0000-000000000035", "00000000-0000-0000-0000-000000000003"), (diff.Changes[1].WhatId, diff.Changes[1].WhoId));
        Assert.Equal(
            [
                "sites is partial in 2026-10-31T060000Z.sqlite (1 item(s) not read): the sites and sharedItems rows of those items may show as removed or added",
                "sites is partial in 2026-11-01T060000Z.sqlite (1 item(s) not read): the sites and sharedItems rows of those items may show as removed or added",
            ],
            diff.Warnings);
    }

    [Fact]
    public async Task DaveGainsAnOwnershipAndAMailboxAndLosesARightOfAGroup()
    {
        string home = await HomeWithTwoSnapshotsAsync();

        SnapshotDiffReport diff = SnapshotDiff.Compare(home, "contoso", First, Second, Now, "00000000-0000-0000-0000-000000000005");

        Assert.Equal("dave@contoso.com", diff.User!.UserPrincipalName);
        Assert.Equal(
            [
                "owners added Project Phoenix | dave@contoso.com | owner (graph) | direct",
                "sites added Project Phoenix | dave@contoso.com | owner | Project Phoenix",
                "mailboxes added finance@contoso.com | dave@contoso.com | full-access | direct",
                "mailboxes removed reception@contoso.com | Finance Staff | full-access | Finance Staff",
            ],
            Lines(diff));
    }

    [Fact]
    public async Task TheEnvelopeNamesTheUserAndTheThroughGroups()
    {
        string home = await HomeWithTwoSnapshotsAsync();

        SnapshotDiffReport diff = SnapshotDiff.Compare(home, "contoso", "2026-10-31", "latest", Now, "bob@contoso.com");

        await Verify(EnvelopeJson.Serialize(CommandResult.Success(diff).ToEnvelope(new EnvelopeMeta([], 3, Backend.Local))), extension: "json");
    }

    [Fact]
    public async Task AUserDiffOfASnapshotWithItselfIsEmpty()
    {
        string home = await HomeWithTwoSnapshotsAsync();

        SnapshotDiffReport diff = SnapshotDiff.Compare(home, "contoso", "latest", "latest", Now, "dave@contoso.com");

        Assert.Empty(diff.Changes);
        Assert.All(diff.Domains, domain => Assert.True(domain.Compared));
    }

    [Fact]
    public async Task ARenamedUserIsFoundInBothSnapshotsByTheObjectId()
    {
        string home = await HomeWithTwoSnapshotsAsync();
        SnapshotSamples.Execute(Second_(home), """
            update objects set userPrincipalName = 'robert@contoso.com', mail = 'robert@contoso.com' where objectId = '00000000-0000-0000-0000-000000000003';
            """);

        SnapshotDiffReport byOldName = SnapshotDiff.Compare(home, "contoso", "yesterday", "latest", Now, "bob@contoso.com");
        SnapshotDiffReport byNewName = SnapshotDiff.Compare(home, "contoso", "yesterday", "latest", Now, "robert@contoso.com");

        // The second snapshot names him: the user is shown as it knows him, and his rows are the same rows as above.
        Assert.Equal("robert@contoso.com", byOldName.User!.UserPrincipalName);
        Assert.Equal(Lines(byOldName), Lines(byNewName));
        Assert.Equal((7, 1), (byOldName.Added, byOldName.Removed));
        Assert.DoesNotContain(byOldName.Warnings, warning => warning.StartsWith("the user", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AUserOneSnapshotDoesNotKnowHasEverythingAddedOrRemoved()
    {
        string home = await HomeWithTwoSnapshotsAsync();
        SnapshotSamples.Execute(Second_(home), "delete from objects where objectId = '00000000-0000-0000-0000-000000000007';");

        SnapshotDiffReport gone = SnapshotDiff.Compare(home, "contoso", "yesterday", "latest", Now, "eve@contoso.com");
        SnapshotDiffReport back = SnapshotDiff.Compare(home, "contoso", "latest", "yesterday", Now, "eve@contoso.com");

        Assert.Equal("eve@contoso.com", gone.User!.UserPrincipalName);
        Assert.Equal(
            [
                "groups removed Project Phoenix | eve@contoso.com | member | direct",
                "groups removed Sales Synced | eve@contoso.com | member | direct",
                "sites removed Project Phoenix | eve@contoso.com | member | Project Phoenix",
            ],
            Lines(gone));
        Assert.Contains($"the user 'eve@contoso.com' is not in {Second}: what they reached in {First} is listed as removed", gone.Warnings);
        Assert.Equal(3, back.Added);
        Assert.Contains($"the user 'eve@contoso.com' is not in {Second}: what they reach in {First} is listed as added", back.Warnings);
    }

    [Fact]
    public async Task AnExternalAddressIsFoundByItsAddress()
    {
        string home = await HomeWithTwoSnapshotsAsync();

        SnapshotDiffReport diff = SnapshotDiff.Compare(home, "contoso", "yesterday", "latest", Now, "PARTNER@fabrikam.com");

        Assert.Equal(new AccessUser(null, "Partner Example", null, "partner@fabrikam.com"), diff.User);
        Assert.Empty(diff.Changes);
    }

    [Fact]
    public async Task AUserNeitherSnapshotKnowsIsNotFoundAndABlankOneIsAUsageError()
    {
        string home = await HomeWithTwoSnapshotsAsync();

        AegisException unknown = Assert.Throws<AegisException>(() => SnapshotDiff.Compare(home, "contoso", "yesterday", "latest", Now, "nobody@contoso.com"));
        AegisException blank = Assert.Throws<AegisException>(() => SnapshotDiff.Compare(home, "contoso", "yesterday", "latest", Now, " "));

        Assert.Equal((FailureKind.NotFound, $"neither {First} nor {Second} knows a user 'nobody@contoso.com'"), (unknown.Failure.Kind, unknown.Message));
        Assert.Equal((FailureKind.Usage, "no user was named"), (blank.Failure.Kind, blank.Message));
    }

    [Fact]
    public async Task ASectionWhoseNestedMembershipsWereNotReadIsLeftOut()
    {
        string home = await HomeWithTwoSnapshotsAsync();
        SnapshotSamples.Execute(Second_(home), """
            update manifest set value = replace(value, '"status":"ok"', '"status":"failed"') where key = 'domain.userGroups';
            """);

        SnapshotDiffReport diff = SnapshotDiff.Compare(home, "contoso", "yesterday", "latest", Now, "bob@contoso.com");

        Assert.Equal(
            ["groups False", "owners True", "sites False", "sharedItems False", "mailboxes False", "calendars False", "forwarding True"],
            diff.Domains.Select(domain => $"{domain.Name} {domain.Compared}"));
        Assert.Contains($"groups is left out: userGroups was not read in {Second}", diff.Warnings);
        SnapshotSamples.Execute(Second_(home), """
            update manifest set value = replace(value, '"status":"failed"', '"status":"partial"') where key = 'domain.userGroups';
            """);
        Assert.Contains(
            $"userGroups is partial in {Second} (items not read: unknown): the groups, sites, sharedItems, mailboxes and calendars rows of those items may show as removed or added",
            SnapshotDiff.Compare(home, "contoso", "yesterday", "latest", Now, "bob@contoso.com").Warnings);
        Assert.Equal(["forwarding"], diff.Changes.Select(change => change.Domain).Distinct());
    }

    [Fact]
    public async Task TheWindowComparesTheNewestWithTheOneBeforeIt()
    {
        string home = await HomeWithTwoSnapshotsAsync();
        // A damaged file between the two is skipped, as `snapshot list` skips it.
        await File.WriteAllTextAsync(Path.Combine(SnapshotStore.Folder(home, "contoso"), "2026-10-31T120000Z.sqlite"), "damaged", TestContext.Current.CancellationToken);

        SnapshotDiffReport? latest = SnapshotDiff.CompareWithThePrevious(home, "contoso", null, " ", null, Now);
        SnapshotDiffReport? named = SnapshotDiff.CompareWithThePrevious(home, "contoso", "", Second, "bob@contoso.com", Now);
        SnapshotDiffReport? both = SnapshotDiff.CompareWithThePrevious(home, "contoso", Second, First, null, Now);

        Assert.Equal((First, Second, "latest"), (latest!.From.Snapshot, latest.To.Snapshot, latest.To.Pick));
        Assert.Equal(14, latest.Changes.Count);
        Assert.Equal((First, Second, 8), (named!.From.Snapshot, named.To.Snapshot, named.Changes.Count));
        Assert.Equal((Second, First), (both!.From.Snapshot, both.To.Snapshot));
    }

    [Fact]
    public async Task TheWindowHasNothingToCompareWithFewerThanTwoSnapshots()
    {
        string none = SnapshotSamples.WriteProfile(PlanSamples.TempHome());
        string one = SnapshotSamples.WriteProfile(PlanSamples.TempHome());
        await SnapshotSamples.SeedAsync(one);
        string two = await HomeWithTwoSnapshotsAsync();

        Assert.Null(SnapshotDiff.CompareWithThePrevious(none, "contoso", null, null, null, Now));
        Assert.Null(SnapshotDiff.CompareWithThePrevious(one, "contoso", null, null, null, Now));
        Assert.Null(SnapshotDiff.CompareWithThePrevious(two, "contoso", null, First, null, Now));
        Assert.Throws<AegisException>(() => SnapshotDiff.CompareWithThePrevious(none, "contoso", null, "latest", null, Now));
    }
}
