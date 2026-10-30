using M365Aegis.Core.Audit;
using M365Aegis.Core.Graph;
using M365Aegis.Core.Output;
using M365Aegis.Core.Tests.Graph;

namespace M365Aegis.Core.Tests.Audit;

/// <summary>The audit lookups over answers that leave fields out: every reading has a fallback, none throws.</summary>
public sealed class AuditEdgeTests
{
    private static readonly DateTimeOffset Now = AuditLookupTests.Now;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ACredentialHolderWithoutNamesIsListedByWhatItHas()
    {
        string end = Now.AddDays(3).ToString("O");
        var graph = Client(request => request.Uri.AbsolutePath.EndsWith("/applications", StringComparison.Ordinal)
            ? $$"""
                {"value":[
                  {"id":"00000000-0000-0000-0000-000000000031"},
                  {"appId":"00000000-0000-0000-0000-000000000032","passwordCredentials":[{"endDateTime":"{{end}}","displayName":" "}]},
                  {"id":"00000000-0000-0000-0000-000000000033","keyCredentials":[{"endDateTime":"{{end}}"}]},
                  {"keyCredentials":[{"endDateTime":"{{end}}","displayName":"signing"}]}]}
                """
            : """{"value":[]}""");

        CommandResult<CredentialExpiryReport> result = await CredentialExpiryLookup.RunAsync(graph, new CredentialExpiryQuery(30, false), Now, Ct);

        IReadOnlyList<ExpiringCredential> credentials = result.Data!.Credentials;
        Assert.Equal(4, result.Data.Applications);
        Assert.Equal(3, credentials.Count);
        Assert.Contains(credentials, credential => credential.DisplayName == "00000000-0000-0000-0000-000000000032" && credential.ObjectId == "" && credential.Name is null);
        Assert.Contains(credentials, credential => credential.DisplayName == "00000000-0000-0000-0000-000000000033" && credential.Kind == CredentialKind.Certificate);
        Assert.Contains(credentials, credential => credential.DisplayName == "" && credential.Name == "signing");
    }

    [Fact]
    public async Task AnEmptyActivityIsAUsageError()
    {
        AegisException exception = await Assert.ThrowsAsync<AegisException>(() =>
            DirectoryAuditLookup.RunAsync(Client(_ => """{"value":[]}"""), new DirectoryAuditQuery(1, null, "  ", 10), Now, Ct));

        Assert.Equal(FailureKind.Usage, exception.Failure.Kind);
    }

    [Fact]
    public async Task AnAuditEntryWithoutFieldsIsAnEmptyEntry()
    {
        var graph = Client(_ => """
            {"value":[
              {},
              {"initiatedBy":{},"targetResources":[{"userPrincipalName":"bob@contoso.com"},{}]}]}
            """);

        CommandResult<DirectoryAuditReport> result = await DirectoryAuditLookup.RunAsync(graph, new DirectoryAuditQuery(1, null, null, 10), Now, Ct);

        Assert.Collection(
            result.Data!.Entries,
            entry =>
            {
                Assert.Equal("", entry.Id);
                Assert.Equal("", entry.ActivityDisplayName);
                Assert.Null(entry.InitiatedBy);
                Assert.Empty(entry.Targets);
            },
            entry =>
            {
                Assert.Null(entry.InitiatedBy);
                Assert.Equal(["bob@contoso.com"], entry.Targets);
            });
    }

    [Fact]
    public async Task ASignInWithoutFieldsIsAnEmptySuccess()
    {
        var graph = Client(_ => """
            {"value":[
              {},
              {"status":{"errorCode":0},"location":{"city":" "},"resourceDisplayName":"Microsoft Graph"}]}
            """);

        CommandResult<SignInReport> result = await SignInLookup.RunAsync(graph, new SignInQuery(null, false, 1, null, 10), Now, Ct);

        Assert.Collection(
            result.Data!.SignIns,
            entry =>
            {
                Assert.Equal("", entry.Id);
                Assert.Null(entry.Location);
                Assert.True(entry.Succeeded);
                Assert.Null(entry.FailureReason);
                Assert.Null(entry.ResourceDisplayName);
            },
            entry =>
            {
                Assert.Null(entry.Location);
                Assert.Equal("Microsoft Graph", entry.ResourceDisplayName);
            });
    }

    [Fact]
    public void AKindOutsideTheEnumHasNoEventType()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ((SignInKind)99).EventType());
        Assert.False(SignInKind.Interactive.IsApplication());
        Assert.True(SignInKind.ManagedIdentity.IsApplication());
    }

    private static GraphClient Client(Func<GraphHttpRequest, string> body) =>
        new(new ScriptedTransport(request => new GraphResponse(200, [], body(request))));
}
