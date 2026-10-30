using M365Aegis.Core.Exchange;
using M365Aegis.Core.Exchange.AdminApi;
using M365Aegis.Core.Exchange.Sidecar;
using M365Aegis.Core.Graph;
using M365Aegis.Core.Output;
using M365Aegis.Core.Tests.Graph;
using M365Aegis.Tests;

namespace M365Aegis.Core.Tests.Exchange;

/// <summary>
/// <c>mailbox list</c> over the layers (task 048): the Admin API's paged <c>Get-Mailbox</c> from the recorded answers
/// and no sidecar started, the shared ones kept from the whole list, and the sidecar over the fake module when the
/// run has no Admin API or the Admin API cannot answer.
/// </summary>
public sealed class MailboxListLayersTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(false, new[] { "Alice Example", "Bob Example", "Carol Example", "Dave Example", "Finance", "Reception" })]
    [InlineData(true, new[] { "Finance", "Reception" })]
    public async Task TheListComesFromTheAdminApiOverEveryPageWithoutASidecar(bool shared, string[] expected)
    {
        var transport = new FixtureGraphTransport(GraphFixtures.Root);
        var admin = new AdminApiClient(transport, _ => Task.FromResult(new AdminApiTarget(AdminApiConnection.TestTenantId, AdminApiConnection.TestAccount)));
        await using ExchangeSession sidecar = SidecarFixtures.Session();
        await using var backends = new ExchangeBackends(sidecar, admin);

        CommandResult<MailboxList> result = await MailboxLookups.ListAsync(backends, shared, Ct);

        Assert.True(result.Ok);
        Assert.Equal(expected, result.Data!.Mailboxes.Select(mailbox => mailbox.DisplayName));
        Assert.Equal(shared, result.Data.SharedOnly);
        Assert.Equal("admin-api", backends.Backend);
        Assert.Equal(0, sidecar.Starts);
        Assert.Equal(2, transport.Requests.Count);
        Assert.All(transport.Requests, request => Assert.DoesNotContain("RecipientTypeDetails\"", request.Body, StringComparison.Ordinal));
        Assert.Contains("$select=DisplayName", Uri.UnescapeDataString(transport.Requests[0].Uri.ToString()), StringComparison.Ordinal);
        MailboxInfo finance = result.Data.Mailboxes.Single(mailbox => mailbox.DisplayName == "Finance");
        Assert.Equal(("finance@contoso.com", "SharedMailbox", "00000000-0000-0000-0000-000000000015"), (finance.PrimarySmtpAddress, finance.Type, finance.ObjectId));
    }

    [Fact]
    public async Task TheAdminApiListEqualsTheSidecarsList()
    {
        await using ExchangeSession sidecar = SidecarFixtures.Session();
        await using var layered = new ExchangeBackends(sidecar, AdminApiConnection.Fixtures(GraphFixtures.Root));
        await using var sidecarOnly = new ExchangeBackends(sidecar);

        CommandResult<MailboxList> fromAdminApi = await MailboxLookups.ListAsync(layered, sharedOnly: false, Ct);
        CommandResult<MailboxList> fromSidecar = await MailboxLookups.ListAsync(sidecarOnly, sharedOnly: false, Ct);

        Assert.Equal(fromSidecar.Data!.Mailboxes, fromAdminApi.Data!.Mailboxes);
        Assert.Equal("pwsh", sidecarOnly.Backend);
    }

    [Theory]
    [InlineData(403)]
    [InlineData(500)]
    public async Task AnAdminApiThatCannotAnswerLeavesTheListToTheSidecar(int status)
    {
        var transport = new ScriptedTransport(_ => new GraphResponse(status, [], """{"error":{"code":"Refused","message":"refused on purpose"}}"""));
        var admin = new AdminApiClient(
            transport,
            _ => Task.FromResult(new AdminApiTarget(AdminApiConnection.TestTenantId, AdminApiConnection.TestAccount)),
            options: new GraphClientOptions { Delay = (_, _) => Task.CompletedTask });
        await using ExchangeSession sidecar = SidecarFixtures.Session();
        await using var backends = new ExchangeBackends(sidecar, admin);

        CommandResult<MailboxList> result = await MailboxLookups.ListAsync(backends, sharedOnly: true, Ct);

        Assert.Equal(["Finance", "Reception"], result.Data!.Mailboxes.Select(mailbox => mailbox.DisplayName));
        Assert.Equal("admin-api+pwsh", backends.Backend);
        Assert.Equal(1, sidecar.Starts);
    }

    [Fact]
    public async Task ARefusedSignInIsNotAskedOfTheSidecarAgain()
    {
        var transport = new ScriptedTransport(_ => new GraphResponse(401, [], """{"error":{"code":"InvalidToken","message":"the token was refused"}}"""));
        var admin = new AdminApiClient(transport, _ => Task.FromResult(new AdminApiTarget(AdminApiConnection.TestTenantId, AdminApiConnection.TestAccount)));
        await using ExchangeSession sidecar = SidecarFixtures.Session();
        await using var backends = new ExchangeBackends(sidecar, admin);

        AegisException exception = await Assert.ThrowsAsync<AegisException>(() => MailboxLookups.ListAsync(backends, sharedOnly: false, Ct));

        Assert.Equal(FailureKind.Auth, exception.Failure.Kind);
        Assert.Equal(0, sidecar.Starts);
    }
}
