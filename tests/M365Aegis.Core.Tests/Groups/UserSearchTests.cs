using System.Text.Json;
using M365Aegis.Core.Graph;
using M365Aegis.Core.Groups;
using M365Aegis.Core.Output;
using M365Aegis.Core.Snapshots;
using M365Aegis.Tests;

namespace M365Aegis.Core.Tests.Groups;

/// <summary>
/// The window's live user search (task 112): one <c>/users</c> request with <c>$search</c> over the display name, the
/// user principal name and the address, an advanced query, nine at most so a ninth marks <c>more</c>.
/// </summary>
public sealed class UserSearchTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    [Fact]
    public async Task TheDirectoryAnswersOneAdvancedQueryOverTheThreeNames()
    {
        (GraphClient graph, FixtureGraphTransport transport) = GraphFixtures.Client();

        UserMatches matches = await UserSearch.FindAsync(graph, "exa", None);

        Assert.Equal((UserSource.Graph, null, null, false), (matches.Source, matches.Snapshot, matches.Taken, matches.More));
        Assert.Equal(new AccessUser("00000000-0000-0000-0000-000000000004", "Carol Example", "carol@contoso.com", null), matches.Users[2]);
        GraphHttpRequest request = Assert.Single(transport.Requests);
        Assert.Equal("eventual", request.Headers["ConsistencyLevel"]);
        string query = Uri.UnescapeDataString(request.Uri.Query);
        Assert.Contains("$search=\"displayName:exa\" OR \"userPrincipalName:exa\" OR \"mail:exa\"", query, StringComparison.Ordinal);
        Assert.Contains("$top=9", query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ANinthUserMarksMoreAndAQuoteIsDropped()
    {
        var transport = new AnsweringTransport(JsonSerializer.Serialize(new
        {
            value = Enumerable.Range(1, 9).Select(n => new { id = $"00000000-0000-0000-0000-0000000000{n:D2}", displayName = $"User {n}" }),
        }));

        UserMatches matches = await UserSearch.FindAsync(new GraphClient(transport), " us\"er ", None);

        Assert.Equal(8, matches.Users.Count);
        Assert.True(matches.More);
        Assert.Contains("\"displayName:user\"", Uri.UnescapeDataString(transport.Asked!.Query), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnEmptyAnswerIsNoUserAndATextOfQuotesIsAUsageError()
    {
        UserMatches none = await UserSearch.FindAsync(new GraphClient(new AnsweringTransport("")), "zzz", None);
        AegisException quotes = await Assert.ThrowsAsync<AegisException>(() => UserSearch.FindAsync(new GraphClient(new AnsweringTransport("")), "\" \"", None));

        Assert.Empty(none.Users);
        Assert.False(none.More);
        Assert.Equal(FailureKind.Usage, quotes.Failure.Kind);
    }

    /// <summary>A transport that answers every request with 200 and the body, and keeps the last address asked.</summary>
    private sealed class AnsweringTransport(string body) : IGraphTransport
    {
        public Uri? Asked { get; private set; }

        public Task<GraphResponse> SendAsync(GraphHttpRequest request, CancellationToken cancellationToken)
        {
            Asked = request.Uri;
            return Task.FromResult(new GraphResponse(200, [], body));
        }
    }
}
