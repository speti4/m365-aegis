using M365Aegis.Core.Exchange;
using M365Aegis.Core.Exchange.Sidecar;
using M365Aegis.Core.Output;
using M365Aegis.Tests;

namespace M365Aegis.Core.Tests.Exchange;

/// <summary>One sidecar over the fake module for every mailbox lookup test, as the window would keep it.</summary>
public sealed class FakeExchange : IAsyncDisposable
{
    /// <summary>The session; it starts on the first cmdlet.</summary>
    public ExchangeSession Session { get; } = SidecarFixtures.Session();

    /// <inheritdoc/>
    public ValueTask DisposeAsync() => Session.DisposeAsync();
}

/// <summary>
/// The mailbox lookups of tasks 015 and 016 against the fake module's mailboxes (tests/sidecar/README.md has the
/// table): the list, access in both directions, the forwarders, the sizes, and their envelopes byte for byte.
/// </summary>
public sealed class MailboxLookupsTests(FakeExchange exchange) : IClassFixture<FakeExchange>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private IExoCmdletRunner Exo => exchange.Session;

    [Fact]
    public async Task TheListIsEveryMailboxByName()
    {
        CommandResult<MailboxList> result = await MailboxLookups.ListAsync(Exo, sharedOnly: false, Ct);

        Assert.True(result.Ok);
        Assert.Equal(["Alice Example", "Bob Example", "Carol Example", "Dave Example", "Finance", "Reception"], result.Data!.Mailboxes.Select(mailbox => mailbox.DisplayName));
    }

    [Fact]
    public async Task TheSharedListIsTheSharedMailboxesOnly()
    {
        CommandResult<MailboxList> result = await MailboxLookups.ListAsync(Exo, sharedOnly: true, Ct);

        Assert.Equal(["finance@contoso.com", "reception@contoso.com"], result.Data!.Mailboxes.Select(mailbox => mailbox.PrimarySmtpAddress));
        Assert.All(result.Data.Mailboxes, mailbox => Assert.Equal("SharedMailbox", mailbox.Type));
    }

    [Fact]
    public async Task AccessListsTheDirectGrantsAndResolvesSendOnBehalf()
    {
        CommandResult<MailboxAccess> result = await MailboxLookups.AccessAsync(Exo, "finance@contoso.com", Ct);

        MailboxAccess access = result.Data!;
        Assert.Equal("Finance", access.Mailbox.DisplayName);
        Assert.Equal(
            [(MailboxRight.FullAccess, "alice@contoso.com"), (MailboxRight.SendAs, "alice@contoso.com"), (MailboxRight.SendOnBehalf, "Bob Example")],
            access.Holders.Select(holder => (holder.Right, holder.Holder)));
        Assert.Equal("bob@contoso.com", access.Holders[2].Address);
    }

    [Fact]
    public async Task AccessLeavesOutTheSelfTheInheritedAndTheDeniedEntries()
    {
        CommandResult<MailboxAccess> alice = await MailboxLookups.AccessAsync(Exo, "alice", Ct);
        CommandResult<MailboxAccess> bob = await MailboxLookups.AccessAsync(Exo, "bob", Ct);

        Assert.Empty(alice.Data!.Holders);
        Assert.Empty(bob.Data!.Holders);
    }

    [Fact]
    public async Task AccessToAMailboxThatDoesNotExistIsNotFound()
    {
        AegisException exception = await Assert.ThrowsAsync<AegisException>(() => MailboxLookups.AccessAsync(Exo, "nobody", Ct));

        Assert.Equal(ExitCode.NotFound, exception.Failure.ExitCode);
        Assert.Equal("mailbox 'nobody' not found", exception.Failure.Message);
    }

    [Fact]
    public async Task TheReverseLookupFindsEveryMailboxTheUserHoldsARightOn()
    {
        CommandResult<UserAccess> alice = await MailboxLookups.AccessForUserAsync(Exo, "alice@contoso.com", Ct);
        CommandResult<UserAccess> bob = await MailboxLookups.AccessForUserAsync(Exo, "bob@contoso.com", Ct);

        Assert.True(alice.Ok);
        Assert.Equal("Alice Example", alice.Data!.DisplayName);
        Assert.Equal(6, alice.Data.MailboxesScanned);
        Assert.Equal(
            [("finance@contoso.com", MailboxRight.FullAccess), ("finance@contoso.com", MailboxRight.SendAs), ("reception@contoso.com", MailboxRight.FullAccess)],
            alice.Data.Grants.Select(grant => (grant.Mailbox.PrimarySmtpAddress, grant.Right)));
        Assert.Equal(
            [("finance@contoso.com", MailboxRight.SendOnBehalf), ("reception@contoso.com", MailboxRight.FullAccess), ("reception@contoso.com", MailboxRight.SendAs)],
            bob.Data!.Grants.Select(grant => (grant.Mailbox.PrimarySmtpAddress, grant.Right)));
    }

    [Fact]
    public async Task TheReverseLookupOfAnUnknownUserIsAnEmptyAnswer()
    {
        CommandResult<UserAccess> result = await MailboxLookups.AccessForUserAsync(Exo, "nobody@contoso.com", Ct);

        Assert.True(result.Ok);
        Assert.Empty(result.Data!.Grants);
        Assert.Null(result.Data.DisplayName);
    }

    [Fact]
    public async Task TheForwardersFlagTheExternalTargetsFirst()
    {
        CommandResult<MailboxForwarders> result = await MailboxLookups.ForwardersAsync(Exo, Ct);

        MailboxForwarders data = result.Data!;
        Assert.Equal(["contoso.com", "contoso.onmicrosoft.com"], data.AcceptedDomains);
        Assert.Equal(
            [("bob@contoso.com", "bob@fabrikam.com", (bool?)true, true), ("dave@contoso.com", "dave@example.com", true, false), ("carol@contoso.com", "finance@contoso.com", false, false)],
            data.Forwarders.Select(forwarder => (forwarder.Mailbox.PrimarySmtpAddress, forwarder.TargetAddress, forwarder.External, forwarder.KeepsCopy)));
        Assert.Equal(ForwardingKind.SmtpAddress, data.Forwarders[0].Kind);
        Assert.Equal("Dave Private", data.Forwarders[1].Target);
    }

    [Fact]
    public async Task TheSizesAreTheLargestFirst()
    {
        CommandResult<MailboxSizes> result = await MailboxLookups.SizeAsync(Exo, null, top: 2, Ct);

        Assert.Equal(6, result.Data!.MailboxesScanned);
        Assert.Equal([3221225472L, 1288490188L], result.Data.Mailboxes.Select(size => size.TotalItemBytes!.Value));
        Assert.Equal("Finance", result.Data.Mailboxes[0].Mailbox.DisplayName);
    }

    [Fact]
    public async Task TheSizeOfOneMailbox()
    {
        CommandResult<MailboxSizes> result = await MailboxLookups.SizeAsync(Exo, "carol@contoso.com", null, Ct);

        MailboxSize size = Assert.Single(result.Data!.Mailboxes);
        Assert.Equal(300, size.ItemCount);
        Assert.Equal(10485760, size.TotalItemBytes);
        Assert.Equal(1536, size.DeletedItemBytes);
    }

    [Fact]
    public async Task ATopOfZeroIsAUsageError()
    {
        AegisException exception = await Assert.ThrowsAsync<AegisException>(() => MailboxLookups.SizeAsync(Exo, null, top: 0, Ct));

        Assert.Equal(FailureKind.Usage, exception.Failure.Kind);
    }

    [Theory]
    [InlineData("1.2 GB (1,288,490,188 bytes)", 1288490188L)]
    [InlineData("0 B (0 bytes)", 0L)]
    [InlineData("1,5 KB (1.536 bytes)", 1536L)]
    [InlineData("1.5 KB (1 536 bytes)", 1536L)]
    [InlineData("Unlimited", null)]
    [InlineData(null, null)]
    public void TheBytesOfASizeAreReadFromItsText(string? text, long? bytes) =>
        Assert.Equal(bytes, MailboxLookups.Bytes(text));

    [Theory]
    [InlineData("bob@fabrikam.com", true)]
    [InlineData("alice@contoso.com", false)]
    [InlineData("alice@CONTOSO.onmicrosoft.com", false)]
    [InlineData("alice@eu.contoso.net", false)]
    [InlineData("alice@contoso.net", true)]
    public void AnAddressIsExternalOutsideTheAcceptedDomains(string address, bool external) =>
        Assert.Equal(external, MailboxLookups.IsExternal(address, ["contoso.com", "contoso.onmicrosoft.com", "*.contoso.net"]));

    [Fact]
    public async Task TheEnvelopesOfTheMailboxLookups()
    {
        var meta = new EnvelopeMeta([], 12, Backend.Pwsh);
        CommandResult<MailboxList> list = await MailboxLookups.ListAsync(Exo, sharedOnly: true, Ct);
        CommandResult<MailboxAccess> access = await MailboxLookups.AccessAsync(Exo, "finance", Ct);
        CommandResult<UserAccess> user = await MailboxLookups.AccessForUserAsync(Exo, "alice@contoso.com", Ct);
        CommandResult<MailboxForwarders> forwarders = await MailboxLookups.ForwardersAsync(Exo, Ct);
        CommandResult<MailboxSizes> sizes = await MailboxLookups.SizeAsync(Exo, null, 2, Ct);

        await Verify(string.Join(
            "\n",
            EnvelopeJson.Serialize(list.ToEnvelope(meta)),
            EnvelopeJson.Serialize(access.ToEnvelope(meta)),
            EnvelopeJson.Serialize(user.ToEnvelope(meta)),
            EnvelopeJson.Serialize(forwarders.ToEnvelope(meta)),
            EnvelopeJson.Serialize(sizes.ToEnvelope(meta))));
    }
}
