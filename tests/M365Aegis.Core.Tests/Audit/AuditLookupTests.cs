using M365Aegis.Core.Audit;
using M365Aegis.Core.Graph;
using M365Aegis.Core.Output;
using M365Aegis.Tests;

namespace M365Aegis.Core.Tests.Audit;

/// <summary>
/// The audit lookups of task 006 over the recorded answers, at a fixed moment: the window, the filters, the pages,
/// the client-side picks (the failures, the expiry), the cap, and the 403 of a tenant without Microsoft Entra ID P1.
/// </summary>
public sealed class AuditLookupTests
{
    /// <summary>The moment every lookup here runs at: the fixtures' window starts a day earlier.</summary>
    internal static readonly DateTimeOffset Now = new(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);

    private static readonly CancellationToken None = CancellationToken.None;

    [Fact]
    public void TheWindowIsOneDayUnlessToldOtherwise()
    {
        Assert.Equal(Now.AddDays(-1), AuditWindow.Start(null, null, Now));
        Assert.Equal(Now.AddDays(-7), AuditWindow.Start(7, null, Now));
        Assert.Equal(new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero), AuditWindow.Start(null, new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero), Now));
    }

    [Fact]
    public void TheStartIsCutToWholeSecondsInUtc()
    {
        var now = new DateTimeOffset(2026, 10, 29, 14, 30, 15, 789, TimeSpan.FromHours(2));

        DateTimeOffset start = AuditWindow.Start(1, null, now);

        Assert.Equal(TimeSpan.Zero, start.Offset);
        Assert.Equal("2026-10-28T12:30:15Z", AuditWindow.Literal(start));
    }

    [Theory]
    [InlineData(0, null)]
    [InlineData(31, null)]
    [InlineData(1, "2026-10-01")]
    [InlineData(null, "2026-11-01")]
    public void AWrongWindowIsAUsageError(int? days, string? since)
    {
        AegisException exception = Assert.Throws<AegisException>(() => AuditWindow.Start(days, AuditWindow.ParseSince(since), Now));

        Assert.Equal(FailureKind.Usage, exception.Failure.Kind);
    }

    [Theory]
    [InlineData("2026-10-01", "2026-10-01T00:00:00Z")]
    [InlineData("2026-10-01T08:00:00Z", "2026-10-01T08:00:00Z")]
    [InlineData("2026-10-01T08:00:00+02:00", "2026-10-01T06:00:00Z")]
    public void SinceIsUtcUnlessItNamesAnOffset(string value, string literal)
    {
        Assert.Equal(literal, AuditWindow.Literal(AuditWindow.ParseSince(value)!.Value));
    }

    [Fact]
    public void SinceThatIsNotADateIsAUsageError()
    {
        AegisException exception = Assert.Throws<AegisException>(() => AuditWindow.ParseSince("last monday"));

        Assert.Equal(FailureKind.Usage, exception.Failure.Kind);
    }

    [Fact]
    public async Task TheSignInsOfAUserComeOverEveryPageNewestFirst()
    {
        (GraphClient graph, FixtureGraphTransport transport) = GraphFixtures.Client();

        CommandResult<SignInReport> result = await SignInLookup.RunAsync(graph, new SignInQuery("Alice@Contoso.com", false, 1, null, 1000), Now, None);

        Assert.True(result.Ok);
        SignInReport report = result.Data!;
        Assert.Equal("alice@contoso.com", report.User);
        Assert.Equal(Now.AddDays(-1), report.Since);
        Assert.False(report.Truncated);
        Assert.Equal(["00000000-0000-0000-0000-0000000000a1", "00000000-0000-0000-0000-0000000000a2", "00000000-0000-0000-0000-0000000000a3", "00000000-0000-0000-0000-0000000000a4"], report.SignIns.Select(entry => entry.Id));
        Assert.Equal(2, transport.Requests.Count);
        Assert.Equal(["00000000-0000-0000-0000-0000000000b1", "00000000-0000-0000-0000-0000000000b2"], graph.RequestLog.Ids);
        SignInEntry failed = report.SignIns[1];
        Assert.False(failed.Succeeded);
        Assert.Equal(50126, failed.ErrorCode);
        Assert.Equal("Vienna, AT", failed.Location);
        Assert.Null(report.SignIns[3].Location);
    }

    [Fact]
    public async Task FailuresAreTheSignInsWithAnErrorCode()
    {
        (GraphClient graph, _) = GraphFixtures.Client();

        CommandResult<SignInReport> result = await SignInLookup.RunAsync(graph, new SignInQuery("alice@contoso.com", true, null, null, 1000), Now, None);

        Assert.Equal([50126, 50074], result.Data!.SignIns.Select(entry => entry.ErrorCode));
        Assert.True(result.Data.FailuresOnly);
    }

    [Fact]
    public async Task TheLimitStopsBeforeTheNextPage()
    {
        (GraphClient graph, FixtureGraphTransport transport) = GraphFixtures.Client();

        CommandResult<SignInReport> result = await SignInLookup.RunAsync(graph, new SignInQuery("alice@contoso.com", false, 1, null, 1), Now, None);

        Assert.Single(result.Data!.SignIns);
        Assert.True(result.Data.Truncated);
        Assert.Single(transport.Requests);
    }

    [Fact]
    public void TheSignInFilterNamesTheWindowAndTheUser()
    {
        var since = new DateTimeOffset(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);

        Assert.Equal(
            "https://graph.microsoft.com/v1.0/auditLogs/signIns?$filter=createdDateTime ge 2026-10-28T12:00:00Z",
            Uri.UnescapeDataString(GraphClient.BuildUri(SignInLookup.Request(null, since)).AbsoluteUri));
        GraphRequest request = SignInLookup.Request("o'brien@contoso.com", since);
        Assert.Equal(
            "https://graph.microsoft.com/v1.0/auditLogs/signIns?$filter=createdDateTime ge 2026-10-28T12:00:00Z and userPrincipalName eq 'o''brien@contoso.com'",
            Uri.UnescapeDataString(GraphClient.BuildUri(request).AbsoluteUri));
        Assert.Equal("AuditLog.Read.All", request.Permission);
    }

    [Fact]
    public async Task ATenantWithoutP1GetsItsOwnSentence()
    {
        (GraphClient graph, _) = GraphFixtures.Client();

        AegisException exception = await Assert.ThrowsAsync<AegisException>(() =>
            SignInLookup.RunAsync(graph, new SignInQuery("bob@contoso.com", false, 1, null, 1000), Now, None));

        Assert.Equal(FailureKind.Forbidden, exception.Failure.Kind);
        Assert.Contains("Microsoft Entra ID P1 or P2", exception.Failure.Message, StringComparison.Ordinal);
        Assert.Contains("audit list", exception.Failure.Hint, StringComparison.Ordinal);
        Assert.Equal(["00000000-0000-0000-0000-0000000000b3"], graph.RequestLog.Ids);
    }

    [Fact]
    public async Task AnyOther403NamesThePermissionAndTheRoles()
    {
        (GraphClient graph, _) = GraphFixtures.Client();

        AegisException exception = await Assert.ThrowsAsync<AegisException>(() =>
            SignInLookup.RunAsync(graph, new SignInQuery("carol@contoso.com", false, 1, null, 1000), Now, None));

        Assert.Equal(FailureKind.Forbidden, exception.Failure.Kind);
        Assert.Contains("Insufficient privileges", exception.Failure.Message, StringComparison.Ordinal);
        Assert.Contains("AuditLog.Read.All", exception.Failure.Hint, StringComparison.Ordinal);
        Assert.Contains("Reports Reader", exception.Failure.Hint, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(" ", 10)]
    [InlineData("alice@contoso.com", 0)]
    public async Task AnEmptyUserOrNoLimitIsAUsageError(string user, int limit)
    {
        (GraphClient graph, FixtureGraphTransport transport) = GraphFixtures.Client();

        AegisException exception = await Assert.ThrowsAsync<AegisException>(() =>
            SignInLookup.RunAsync(graph, new SignInQuery(user, false, 1, null, limit), Now, None));

        Assert.Equal(FailureKind.Usage, exception.Failure.Kind);
        Assert.Empty(transport.Requests);
    }

    [Fact]
    public async Task TheCredentialsThatExpireWithinAMonthComeSoonestFirstWithTheirApplication()
    {
        (GraphClient graph, FixtureGraphTransport transport) = GraphFixtures.Client();

        CommandResult<CredentialExpiryReport> result = await CredentialExpiryLookup.RunAsync(graph, new CredentialExpiryQuery(30, false), Now, None);

        CredentialExpiryReport report = result.Data!;
        Assert.Equal(4, report.Applications);
        Assert.Equal(2, report.ServicePrincipals);
        Assert.Equal(3, transport.Requests.Count);
        Assert.Equal(["Contoso SAML App", "Payroll Connector", "Reporting Bot"], report.Credentials.Select(credential => credential.DisplayName));
        Assert.Equal([2, 11, 29], report.Credentials.Select(credential => credential.DaysLeft));
        Assert.Equal([CredentialOwner.ServicePrincipal, CredentialOwner.Application, CredentialOwner.Application], report.Credentials.Select(credential => credential.Owner));
        Assert.Equal([CredentialKind.Certificate, CredentialKind.Secret, CredentialKind.Secret], report.Credentials.Select(credential => credential.Kind));
        Assert.Equal("production secret", report.Credentials[1].Name);
        Assert.Null(report.Credentials[2].Name);
        Assert.Equal("00000000-0000-0000-0000-000000000064", report.Credentials[2].KeyId);
        Assert.All(report.Credentials, credential => Assert.False(credential.Expired));
    }

    [Fact]
    public async Task TheExpiredCredentialsComeOnlyWhenAskedFor()
    {
        (GraphClient graph, _) = GraphFixtures.Client();

        CommandResult<CredentialExpiryReport> result = await CredentialExpiryLookup.RunAsync(graph, new CredentialExpiryQuery(30, true), Now, None);

        ExpiringCredential first = result.Data!.Credentials[0];
        Assert.Equal("Old Integration", first.DisplayName);
        Assert.True(first.Expired);
        Assert.True(first.DaysLeft < 0);
        Assert.Equal(4, result.Data.Credentials.Count);
    }

    [Fact]
    public void TheCredentialRequestSelectsTheCredentialsAtTheLargestPage()
    {
        GraphRequest request = CredentialExpiryLookup.Request("/servicePrincipals");

        Assert.Equal(
            "https://graph.microsoft.com/v1.0/servicePrincipals?$select=id,appId,displayName,passwordCredentials,keyCredentials&$top=999",
            Uri.UnescapeDataString(GraphClient.BuildUri(request).AbsoluteUri));
        Assert.Equal("Application.Read.All", request.Permission);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3651)]
    public async Task ADayCountOutOfRangeIsAUsageError(int days)
    {
        (GraphClient graph, _) = GraphFixtures.Client();

        AegisException exception = await Assert.ThrowsAsync<AegisException>(() =>
            CredentialExpiryLookup.RunAsync(graph, new CredentialExpiryQuery(days, false), Now, None));

        Assert.Equal(FailureKind.Usage, exception.Failure.Kind);
    }

    [Fact]
    public async Task TheDirectoryAuditNamesWhoDidWhatToWhom()
    {
        (GraphClient graph, _) = GraphFixtures.Client();

        CommandResult<DirectoryAuditReport> result = await DirectoryAuditLookup.RunAsync(graph, new DirectoryAuditQuery(1, null, null, 1000), Now, None);

        DirectoryAuditReport report = result.Data!;
        Assert.Equal(["Add member to group", "Update user", "Reset user password"], report.Entries.Select(entry => entry.ActivityDisplayName));
        Assert.Equal(["alice@contoso.com", "Contoso HR Sync", "alice@contoso.com"], report.Entries.Select(entry => entry.InitiatedBy));
        Assert.Equal(["Finance", "bob@contoso.com"], report.Entries[0].Targets);
        Assert.Null(report.Entries[0].ResultReason);
        Assert.Equal("failure", report.Entries[2].Result);
        Assert.Contains("complexity", report.Entries[2].ResultReason, StringComparison.Ordinal);
        Assert.False(report.Truncated);
    }

    [Fact]
    public async Task TheActivityFilterIsAStartsWith()
    {
        (GraphClient graph, FixtureGraphTransport transport) = GraphFixtures.Client();

        CommandResult<DirectoryAuditReport> result = await DirectoryAuditLookup.RunAsync(graph, new DirectoryAuditQuery(1, null, " Add member ", 1000), Now, None);

        Assert.Equal("Add member", result.Data!.Activity);
        Assert.Single(result.Data.Entries);
        Assert.Contains(
            "startswith(activityDisplayName,'Add member')",
            Uri.UnescapeDataString(Assert.Single(transport.Requests).Uri.AbsoluteUri),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheDirectoryAuditStopsAtTheLimit()
    {
        (GraphClient graph, _) = GraphFixtures.Client();

        CommandResult<DirectoryAuditReport> result = await DirectoryAuditLookup.RunAsync(graph, new DirectoryAuditQuery(1, null, null, 2), Now, None);

        Assert.Equal(2, result.Data!.Entries.Count);
        Assert.True(result.Data.Truncated);
    }

    [Fact]
    public async Task TheEnvelopesOfTheThreeReports()
    {
        (GraphClient signIns, _) = GraphFixtures.Client();
        (GraphClient secrets, _) = GraphFixtures.Client();
        (GraphClient audit, _) = GraphFixtures.Client();

        CommandResult<SignInReport> signInResult = await SignInLookup.RunAsync(signIns, new SignInQuery("alice@contoso.com", true, 1, null, 1000), Now, None);
        CommandResult<CredentialExpiryReport> secretResult = await CredentialExpiryLookup.RunAsync(secrets, new CredentialExpiryQuery(30, true), Now, None);
        CommandResult<DirectoryAuditReport> auditResult = await DirectoryAuditLookup.RunAsync(audit, new DirectoryAuditQuery(1, null, null, 2), Now, None);

        await Verify(string.Join(
            "\n",
            EnvelopeJson.Serialize(signInResult.ToEnvelope(new EnvelopeMeta(signIns.RequestLog.Ids, 12, Backend.Graph))),
            EnvelopeJson.Serialize(secretResult.ToEnvelope(new EnvelopeMeta(secrets.RequestLog.Ids, 12, Backend.Graph))),
            EnvelopeJson.Serialize(auditResult.ToEnvelope(new EnvelopeMeta(audit.RequestLog.Ids, 12, Backend.Graph)))));
    }
}
