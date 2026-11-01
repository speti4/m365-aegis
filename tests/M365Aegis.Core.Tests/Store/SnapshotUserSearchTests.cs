using M365Aegis.Core.Output;
using M365Aegis.Core.Snapshots;
using M365Aegis.Tests;

namespace M365Aegis.Core.Tests.Store;

/// <summary>
/// The user search over the fixture snapshot (task 112), which the window's user picker asks as the colleague types: a
/// prefix of the name, of a word of it, of the user principal name or of the address, in any case, the users only, by
/// name, the limit at most with a mark that more match.
/// </summary>
public sealed class SnapshotUserSearchTests
{
    private static async Task<string> HomeWithTheFixtureSnapshotAsync()
    {
        string home = SnapshotSamples.WriteProfile(PlanSamples.TempHome());
        await SnapshotSamples.SeedAsync(home);
        return home;
    }

    private static IEnumerable<string?> Names(UserMatches matches) => matches.Users.Select(user => user.DisplayName);

    [Fact]
    public async Task TheStartOfANameOrOfAWordOfItFindsTheUserInAnyCase()
    {
        string home = await HomeWithTheFixtureSnapshotAsync();

        UserMatches first = SnapshotAccess.FindUsers(home, "contoso", "dav");
        UserMatches word = SnapshotAccess.FindUsers(home, "contoso", " EXAM ");

        Assert.Equal((UserSource.Snapshot, "2026-10-31T060000Z.sqlite", SnapshotSamples.Taken), (first.Source, first.Snapshot, first.Taken));
        Assert.Equal([new AccessUser("00000000-0000-0000-0000-000000000005", "Dave Example", "dave@contoso.com", "dave@contoso.com")], first.Users);
        Assert.False(first.More);
        Assert.Equal(["Alice Example", "Bob Example", "Carol Example", "Dave Example", "Eve Example"], Names(word));
    }

    [Fact]
    public async Task TheStartOfAnAddressFindsTheUserAndAGroupIsNoMatch()
    {
        string home = await HomeWithTheFixtureSnapshotAsync();

        Assert.Equal(["Carol Example"], Names(SnapshotAccess.FindUsers(home, "contoso", "carol@")));
        // Finance is a group of the snapshot: the picker suggests users only.
        Assert.DoesNotContain("Finance EU", Names(SnapshotAccess.FindUsers(home, "contoso", "fin")));
        // The middle of a word is no start.
        Assert.Empty(SnapshotAccess.FindUsers(home, "contoso", "xample").Users);
    }

    [Fact]
    public async Task MoreMatchesThanTheLimitAreMarked()
    {
        string home = await HomeWithTheFixtureSnapshotAsync();

        UserMatches matches = SnapshotAccess.FindUsers(home, "contoso", "exa", limit: 2);

        Assert.Equal(["Alice Example", "Bob Example"], Names(matches));
        Assert.True(matches.More);
    }

    [Theory]
    [InlineData("%")]
    [InlineData("_")]
    [InlineData(@"\")]
    [InlineData("a%e")]
    public async Task AWildcardInTheTextMatchesItself(string text)
    {
        string home = await HomeWithTheFixtureSnapshotAsync();

        Assert.Empty(SnapshotAccess.FindUsers(home, "contoso", text).Users);
    }

    [Fact]
    public async Task NoSnapshotOrNoTextIsAnAnswerWithAHint()
    {
        string home = await HomeWithTheFixtureSnapshotAsync();

        AegisException none = Assert.Throws<AegisException>(() => SnapshotAccess.FindUsers(PlanSamples.TempHome(), "contoso", "dav"));
        AegisException empty = Assert.Throws<AegisException>(() => SnapshotAccess.FindUsers(home, "contoso", "  "));

        Assert.Equal((FailureKind.NotFound, SnapshotStore.TakeHint), (none.Failure.Kind, none.Failure.Hint));
        Assert.Equal((FailureKind.Usage, "no text to search for", "type the start of a name or an address"), (empty.Failure.Kind, empty.Message, empty.Failure.Hint));
    }
}
