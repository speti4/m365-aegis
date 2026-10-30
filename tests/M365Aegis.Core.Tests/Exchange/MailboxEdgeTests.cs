using System.Text.Json;
using M365Aegis.Core.Diagnostics;
using M365Aegis.Core.Exchange;
using M365Aegis.Core.Exchange.AdminApi;
using M365Aegis.Core.Exchange.Sidecar;
using M365Aegis.Core.Graph;
using M365Aegis.Core.Output;
using M365Aegis.Core.Tests.Config;
using M365Aegis.Core.Tests.Graph;

namespace M365Aegis.Core.Tests.Exchange;

/// <summary>
/// The mailbox reads over answers a test writes itself (<see cref="CannedRunner"/>), for the shapes the fake module and
/// the recorded answers do not hold: a value the service left out, a holder no name resolves, an inherited or denied
/// entry, a failed share of a bulk read, a report whose names are concealed, a quoted CSV field.
/// </summary>
public sealed class MailboxEdgeTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AMailboxIsNamedOrItIsAUsageError()
    {
        AegisException exception = await Assert.ThrowsAsync<AegisException>(() => MailboxLookups.AccessAsync(new CannedRunner(_ => Items()), " ", Ct));

        Assert.Equal(FailureKind.Usage, exception.Failure.Kind);
    }

    [Fact]
    public async Task AccessKeepsTheDirectEntriesOfPeopleAndNamesAnUnresolvedDelegateAsStored()
    {
        var exo = new CannedRunner(command => command.Cmdlet switch
        {
            ExoCmdlets.GetMailbox => Items("""{"Name":"Shared Box","PrimarySmtpAddress":"shared@contoso.com","GrantSendOnBehalfTo":["Ghost","Bob Example"]}"""),
            ExoCmdlets.GetMailboxPermission => Items(
                """{"User":"inherited@contoso.com","AccessRights":["FullAccess"],"IsInherited":true}""",
                """{"User":"denied@contoso.com","AccessRights":["FullAccess"],"Deny":true}""",
                """{"User":"reader@contoso.com","AccessRights":["ReadPermission"]}""",
                """{"AccessRights":["FullAccess"]}""",
                """{"User":"NT AUTHORITY\\SELF","AccessRights":["FullAccess"]}""",
                """{"User":"carol@contoso.com","AccessRights":["FullAccess"]}"""),
            ExoCmdlets.GetRecipientPermission => Items(
                """{"Trustee":"inherited@contoso.com","AccessRights":["SendAs"],"IsInherited":true}""",
                """{"Trustee":"denied@contoso.com","AccessRights":["SendAs"],"AccessControlType":"Deny"}""",
                """{"Trustee":"reader@contoso.com","AccessRights":["ReadPermission"]}""",
                """{"Trustee":"Dave Example","AccessRights":["SendAs"]}"""),
            _ => Items("""{"Name":"Bob Example","PrimarySmtpAddress":"bob@contoso.com"}"""),
        });

        MailboxAccess access = (await MailboxLookups.AccessAsync(exo, "shared", Ct)).Data!;

        Assert.Equal(new MailboxInfo("Shared Box", "shared@contoso.com", "?", null, null), access.Mailbox);
        Assert.Equal("shared@contoso.com", exo.Commands.First(command => command.Cmdlet == ExoCmdlets.GetMailboxPermission).Parameters["Identity"]);
        Assert.Equal(
            [(MailboxRight.FullAccess, "carol@contoso.com", "carol@contoso.com"), (MailboxRight.SendAs, "Dave Example", null), (MailboxRight.SendOnBehalf, "Bob Example", "bob@contoso.com"), (MailboxRight.SendOnBehalf, "Ghost", null)],
            access.Holders.Select(holder => (holder.Right, holder.Holder, holder.Address)));
    }

    [Fact]
    public async Task AMailboxTheServiceAnswersWithNothingIsNotFoundAndAnObjectWithoutNamesIsAQuestionMark()
    {
        AegisException missing = await Assert.ThrowsAsync<AegisException>(() => MailboxLookups.AccessAsync(new CannedRunner(_ => Items()), "nobody", Ct));
        MailboxList list = (await MailboxLookups.ListAsync(new CannedRunner(_ => Items("{}")), sharedOnly: false, Ct)).Data!;

        Assert.Equal(FailureKind.NotFound, missing.Failure.Kind);
        Assert.Equal("mailbox 'nobody' not found", missing.Failure.Message);
        Assert.Equal(new MailboxInfo("?", "-", "?", null, null), Assert.Single(list.Mailboxes));
    }

    [Fact]
    public async Task ForwardersTellTheExternalTargetsAndLeaveAnUnresolvedRecipientOpen()
    {
        var exo = new CannedRunner(command => command.Cmdlet switch
        {
            ExoCmdlets.GetAcceptedDomain => Items("""{"DomainName":"contoso.com"}""", """{"DomainName":"*.contoso.net"}""", "{}"),
            ExoCmdlets.GetMailbox => Items(
                """{"DisplayName":"Alpha","PrimarySmtpAddress":"alpha@contoso.com","ForwardingSmtpAddress":"smtp:ext@fabrikam.com","DeliverToMailboxAndForward":true}""",
                """{"DisplayName":"Beta","PrimarySmtpAddress":"beta@contoso.com","ForwardingSmtpAddress":"in@sub.contoso.net"}""",
                """{"DisplayName":"Gamma","PrimarySmtpAddress":"gamma@contoso.com","ForwardingAddress":"Ghost Contact"}""",
                """{"DisplayName":"Delta","PrimarySmtpAddress":"delta@contoso.com","ForwardingAddress":"Bob Example"}""",
                """{"DisplayName":"Epsilon","PrimarySmtpAddress":"epsilon@contoso.com"}"""),
            _ => Items("""{"Name":"Bob Example","PrimarySmtpAddress":"bob@contoso.com"}"""),
        });

        MailboxForwarders result = (await MailboxLookups.ForwardersAsync(exo, Ct)).Data!;

        Assert.Equal(5, result.MailboxesScanned);
        Assert.Equal(["contoso.com", "*.contoso.net"], result.AcceptedDomains);
        Assert.Equal(
            [("Alpha", "ext@fabrikam.com", (bool?)true, true), ("Gamma", null, null, false), ("Beta", "in@sub.contoso.net", false, false), ("Delta", "bob@contoso.com", false, false)],
            result.Forwarders.Select(forwarder => (forwarder.Mailbox.DisplayName, forwarder.TargetAddress, forwarder.External, forwarder.KeepsCopy)));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("unlimited", null)]
    [InlineData("1.2 GB (99999999999999999999 bytes)", null)]
    [InlineData("1.2 GB (1,288,490,188 bytes)", 1288490188L)]
    public void ASizeIsItsBytesWhenTheyFitALong(string? size, long? bytes) =>
        Assert.Equal(bytes, MailboxLookups.Bytes(size));

    [Fact]
    public void AnAddressWithoutADomainIsNotExternal() =>
        Assert.False(MailboxLookups.IsExternal("no-at-sign", ["contoso.com"]));

    [Fact]
    public async Task TheReverseLookupKeepsTheRecipientsNameAndIsPartialWhenAPermissionListFailed()
    {
        var exo = new CannedRunner(command => command.Cmdlet switch
        {
            ExoCmdlets.GetRecipient => Items("""{"DisplayName":"Carol Example","UserPrincipalName":"carol@contoso.com","PrimarySmtpAddress":"carol@contoso.com"}"""),
            ExoCmdlets.GetMailbox => Items(
                """{"DisplayName":"Carol's Box","PrimarySmtpAddress":"carol@contoso.com","UserPrincipalName":"carol@contoso.com","ExternalDirectoryObjectId":"00000000-0000-0000-0000-000000000061"}""",
                """{"DisplayName":"Shared","PrimarySmtpAddress":"shared@contoso.com","ExternalDirectoryObjectId":"00000000-0000-0000-0000-000000000062"}"""),
            ExoCmdlets.GetMailboxPermission => new ExoResult(
                [
                    Object("""{"Identity":"Shared","User":"carol@contoso.com","AccessRights":["FullAccess"]}"""),
                    Object("""{"Identity":"Gone Box","User":"carol@contoso.com","AccessRights":["FullAccess"]}"""),
                    Object("""{"User":"carol@contoso.com","AccessRights":["FullAccess"]}"""),
                ],
                ["the permissions of one mailbox could not be read"]),
            _ => Items(),
        });

        CommandResult<UserAccess> result = await MailboxLookups.AccessForUserAsync(exo, "carol@contoso.com", Ct);

        Assert.Equal(FailureKind.Partial, result.Failure!.Kind);
        Assert.Equal("1 mailbox permission list(s) could not be read; the answer lists the rest", result.Failure.Message);
        Assert.Equal("Carol Example", result.Data!.DisplayName);
        // The entry without an identity and the one of a mailbox gone since are both "-" by address: one grant stays.
        Assert.Equal(["Gone Box", "Shared"], result.Data.Grants.Select(grant => grant.Mailbox.DisplayName));
    }

    [Fact]
    public async Task AUserExchangeDoesNotKnowAsARecipientTakesTheNameOfTheirMailbox()
    {
        var exo = new CannedRunner(command => command.Cmdlet == ExoCmdlets.GetMailbox
            ? Items("""{"DisplayName":"Carol's Box","PrimarySmtpAddress":"carol@contoso.com","UserPrincipalName":"carol.example@contoso.com"}""")
            : Items());

        UserAccess access = (await MailboxLookups.AccessForUserAsync(exo, "carol@contoso.com", Ct)).Data!;

        Assert.Equal("Carol's Box", access.DisplayName);
        Assert.Empty(access.Grants);
    }

    [Fact]
    public async Task SizesMatchTheirMailboxByGuidThenByNameAndArePartialWhenAMailboxFailed()
    {
        var exo = new CannedRunner(command => command.Cmdlet switch
        {
            ExoCmdlets.GetMailbox => Items(
                """{"DisplayName":"One","PrimarySmtpAddress":"one@contoso.com","ExchangeGuid":"00000000-0000-0000-0000-0000000000e1"}""",
                """{"DisplayName":"Two","PrimarySmtpAddress":"two@contoso.com"}"""),
            _ => new ExoResult(
                [
                    Object("""{"MailboxGuid":"00000000-0000-0000-0000-0000000000e1","ItemCount":3,"TotalItemSize":"1 KB (1,024 bytes)"}"""),
                    Object("""{"MailboxGuid":"00000000-0000-0000-0000-0000000000e9","DisplayName":"Two","ItemCount":2}"""),
                    Object("""{"DisplayName":"Stranger"}"""),
                    Object("{}"),
                ],
                ["one mailbox could not be read"]),
        });

        CommandResult<MailboxSizes> result = await MailboxLookups.SizeAsync(exo, null, null, Ct);

        Assert.Equal(FailureKind.Partial, result.Failure!.Kind);
        Assert.Equal(
            [("One", "one@contoso.com"), ("?", "-"), ("Stranger", "-"), ("Two", "two@contoso.com")],
            result.Data!.Mailboxes.Select(size => (size.Mailbox.DisplayName, size.Mailbox.PrimarySmtpAddress)));
    }

    [Fact]
    public async Task AnAdminApiThatCannotListTheMailboxesIsWrittenToTheVerboseLog()
    {
        string home = ProfileStoreTests.TempHome();
        var admin = new CannedRunner(_ => throw new AegisException(FailureKind.ServiceError, "the service broke", "later"));
        await using var backends = new ExchangeBackends(new CannedRunner(_ => Items("""{"DisplayName":"Sidecar Box","PrimarySmtpAddress":"box@contoso.com"}""")), admin);

        MailboxList list;
        using (LogSink.UseVerbose(LogSink.ForHome(home)))
        {
            list = (await MailboxLookups.ListAsync(backends, sharedOnly: false, Ct)).Data!;
        }

        Assert.Equal("Sidecar Box", Assert.Single(list.Mailboxes).DisplayName);
        Assert.Contains("the Admin API could not list the mailboxes (service-error", ReadLog(home), StringComparison.Ordinal);
    }

    [Fact]
    public async Task FolderPermissionsFallBackToTheEnglishCalendarAndReadAHolderObjectByItsName()
    {
        var sidecar = new CannedRunner(_ => Items("""{"Name":"Inbox","FolderPath":"/Inbox","FolderType":"Inbox"}"""));
        var admin = new CannedRunner(_ => Items(
            """{"User":{"DisplayName":"Bob Example"},"AccessRights":"Reviewer, None","SharingPermissionFlags":"None","FolderName":"Calendar"}""",
            """{"User":{"DisplayName":"  ","Name":5,"Identity":"Default"},"AccessRights":["AvailabilityOnly"]}""",
            """{"User":{"Other":"x"},"AccessRights":["None"]}""",
            """{"User":5}""",
            """{"AccessRights":["None"]}""",
            """{"User":[]}"""));
        await using var backends = new ExchangeBackends(sidecar, admin);

        MailboxFolderPermissions result = (await MailboxLookups.FolderPermissionsAsync(backends, "finance", null, Ct)).Data!;

        Assert.Equal("Calendar", result.Folder);
        Assert.Equal("Calendar", result.FolderName);
        Assert.Equal(["Bob Example", "Default", "?", "5", "?", "?"], result.Permissions.Select(entry => entry.User));
        Assert.Equal(["Reviewer", "None"], result.Permissions[0].AccessRights);
        Assert.Empty(result.Permissions[0].SharingPermissionFlags);
    }

    [Fact]
    public async Task ACalendarNameTheSidecarCannotReadIsTheEnglishOneWithTheReasonInTheLog()
    {
        string home = ProfileStoreTests.TempHome();
        var sidecar = new CannedRunner(_ => throw new AegisException(FailureKind.CmdletFailed, "not here", "later"));
        var admin = new CannedRunner(_ => Items());
        await using var backends = new ExchangeBackends(sidecar, admin);

        MailboxFolderPermissions quiet = (await MailboxLookups.FolderPermissionsAsync(backends, "finance", null, Ct)).Data!;
        MailboxFolderPermissions logged;
        using (LogSink.UseVerbose(LogSink.ForHome(home)))
        {
            logged = (await MailboxLookups.FolderPermissionsAsync(backends, "finance", " ", Ct)).Data!;
        }

        Assert.Equal("Calendar", quiet.Folder);
        Assert.Equal("Calendar", logged.Folder);
        Assert.Contains("the calendar's name could not be read (cmdlet-failed); the English name is tried", ReadLog(home), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnOrganizationWithoutAConfigurationIsEmpty()
    {
        await using var backends = new ExchangeBackends(new CannedRunner(_ => Items()), new CannedRunner(_ => Items()));

        ExchangeOrganization organization = (await MailboxLookups.OrganizationAsync(backends, Ct)).Data!;

        Assert.Null(organization.Name);
        Assert.Null(organization.MailTipsAllTipsEnabled);
        Assert.Null(organization.MailTipsExternalRecipientsTipsEnabled);
        Assert.Null(organization.MailTipsLargeAudienceThreshold);
        Assert.Empty(organization.AcceptedDomains);
    }

    [Theory]
    [InlineData("{not json", "{not json", null)]
    [InlineData("""{"DisplayName":"Bob Example"}""", "Bob Example", null)]
    [InlineData("""{"Other":5}""", """{"Other":5}""", null)]
    [InlineData("<>", "<>", null)]
    [InlineData("""{"PrimarySmtpAddress":"bob@contoso.com"}""", "bob@contoso.com", "bob@contoso.com")]
    [InlineData("<smtp:bob@contoso.com>", "bob@contoso.com", "bob@contoso.com")]
    public void ADelegateOfAnyShapeIsReadAsAHolder(string entry, string holder, string? address) =>
        Assert.Equal((holder, address), MailboxLookups.Delegate(entry));

    [Fact]
    public async Task TheAdminApisPlainDelegatesWithAddressesNeedNoResolving()
    {
        var sidecar = new CannedRunner(command => command.Cmdlet == ExoCmdlets.GetRecipient ? Items("""{"Name":"Carol Example","PrimarySmtpAddress":"carol@contoso.com"}""") : Items());
        var admin = new CannedRunner(_ => Items("""{"DisplayName":"Finance","PrimarySmtpAddress":"finance@contoso.com","GrantSendOnBehalfTo":["bob@contoso.com"]}"""));
        var named = new CannedRunner(_ => Items("""{"DisplayName":"Finance","PrimarySmtpAddress":"finance@contoso.com","GrantSendOnBehalfTo":["bob@contoso.com","Carol Example"]}"""));
        await using var backends = new ExchangeBackends(sidecar, admin);
        await using var withNames = new ExchangeBackends(sidecar, named);

        MailboxAccess access = (await MailboxLookups.AccessAsync(backends, "finance", new MailboxAccessOptions(), Ct)).Data!;
        int lookups = sidecar.Commands.Count(command => command.Cmdlet == ExoCmdlets.GetRecipient);
        MailboxAccess resolved = (await MailboxLookups.AccessAsync(withNames, "finance", new MailboxAccessOptions(), Ct)).Data!;

        MailboxHolder only = Assert.Single(access.Holders);
        Assert.Equal((MailboxRight.SendOnBehalf, "bob@contoso.com", "bob@contoso.com"), (only.Right, only.Holder, only.Address));
        Assert.Equal(0, lookups);
        Assert.Equal(
            [("bob@contoso.com", "bob@contoso.com"), ("Carol Example", "carol@contoso.com")],
            resolved.Holders.Select(holder => (holder.Holder, holder.Address)));
    }

    [Fact]
    public async Task AnAdminApiThatCannotReadTheMailboxIsWrittenToTheVerboseLogAndTheSidecarAnswers()
    {
        string home = ProfileStoreTests.TempHome();
        var sidecar = new CannedRunner(command => command.Cmdlet == ExoCmdlets.GetMailbox ? Items("""{"DisplayName":"Finance","PrimarySmtpAddress":"finance@contoso.com"}""") : Items());
        var admin = new CannedRunner(_ => throw new AegisException(FailureKind.Throttled, "too many", "later"));
        await using var backends = new ExchangeBackends(sidecar, admin);

        MailboxAccess access;
        using (LogSink.UseVerbose(LogSink.ForHome(home)))
        {
            access = (await MailboxLookups.AccessAsync(backends, "finance", new MailboxAccessOptions(), Ct)).Data!;
        }

        Assert.Equal("Finance", access.Mailbox.DisplayName);
        Assert.Contains("the Admin API could not read the mailbox (throttled", ReadLog(home), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RightsTheSidecarCannotReadFailTheAccessThroughTheAdminApi()
    {
        var sidecar = new CannedRunner(_ => throw new AegisException(FailureKind.Forbidden, "no role", "ask for a role"));
        var admin = new CannedRunner(_ => Items("""{"DisplayName":"Finance","PrimarySmtpAddress":"finance@contoso.com"}"""));
        await using var backends = new ExchangeBackends(sidecar, admin);

        AegisException exception = await Assert.ThrowsAsync<AegisException>(() => MailboxLookups.AccessAsync(backends, "finance", new MailboxAccessOptions(), Ct));

        Assert.Equal(FailureKind.Forbidden, exception.Failure.Kind);
    }

    [Fact]
    public async Task AMailboxTheAdminApiAnswersWithNothingIsNotFound()
    {
        await using var backends = new ExchangeBackends(new CannedRunner(_ => Items()), new CannedRunner(_ => Items()));

        AegisException exception = await Assert.ThrowsAsync<AegisException>(() => MailboxLookups.AccessAsync(backends, "nobody", new MailboxAccessOptions(), Ct));

        Assert.Equal("mailbox 'nobody' not found", exception.Failure.Message);
        Assert.Null(exception.InnerException);
    }

    [Fact]
    public async Task ExpandingAMailboxWithoutHoldersAsksNothingMore()
    {
        var sidecar = new CannedRunner(command => command.Cmdlet == ExoCmdlets.GetMailbox ? Items("""{"DisplayName":"Quiet","PrimarySmtpAddress":"quiet@contoso.com"}""") : Items());
        await using var backends = new ExchangeBackends(sidecar);

        MailboxAccess access = (await MailboxLookups.AccessAsync(backends, "quiet", new MailboxAccessOptions(ExpandGroups: true), Ct)).Data!;

        Assert.Empty(access.Holders);
        Assert.DoesNotContain(sidecar.Commands, command => command.Cmdlet == ExoCmdlets.GetRecipient);
    }

    [Fact]
    public async Task WithoutGraphAGroupHolderIsMarkedButNotExpanded()
    {
        var sidecar = new CannedRunner(command => command.Cmdlet switch
        {
            ExoCmdlets.GetMailbox => Items("""{"DisplayName":"Rooms","PrimarySmtpAddress":"rooms@contoso.com"}"""),
            ExoCmdlets.GetMailboxPermission => Items(
                """{"User":"Room Finders","AccessRights":["FullAccess"]}""",
                """{"User":"Ghost","AccessRights":["FullAccess"]}""",
                """{"User":"alice@contoso.com","AccessRights":["FullAccess"]}"""),
            ExoCmdlets.GetRecipient => Items(
                """{"Name":"Room Finders","RecipientTypeDetails":"RoomList","ExternalDirectoryObjectId":"00000000-0000-0000-0000-000000000071"}""",
                """{"Name":"alice","PrimarySmtpAddress":"alice@contoso.com","RecipientTypeDetails":"UserMailbox"}"""),
            _ => Items(),
        });
        await using var backends = new ExchangeBackends(sidecar);

        MailboxAccess access = (await MailboxLookups.AccessAsync(backends, "rooms", new MailboxAccessOptions(ExpandGroups: true), Ct)).Data!;

        Assert.Equal(
            [("alice@contoso.com", (bool?)false), ("Ghost", false), ("Room Finders", true)],
            access.Holders.Select(holder => (holder.Holder, holder.Group)));
        Assert.All(access.Holders, holder => Assert.Null(holder.Members));
    }

    [Fact]
    public async Task TheReverseLookupThroughGroupsNeedsGraph()
    {
        await using var backends = new ExchangeBackends(new CannedRunner(_ => Items()));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            MailboxLookups.AccessForUserAsync(backends, "carol@contoso.com", new UserAccessOptions(ExpandGroups: true), Ct));
    }

    [Fact]
    public async Task AGroupHolderNoNameMatchesIsResolvedToItsObjectId()
    {
        var sidecar = new CannedRunner(command => command.Cmdlet switch
        {
            ExoCmdlets.GetRecipient when command.Input is ["carol@contoso.com"] => Items(),
            ExoCmdlets.GetRecipient => Items(
                """{"Name":"FS-Alias","ExternalDirectoryObjectId":"00000000-0000-0000-0000-000000000035"}""",
                """{"Name":"Elsewhere","ExternalDirectoryObjectId":"00000000-0000-0000-0000-000000000099"}""",
                """{"Name":"No Id"}"""),
            ExoCmdlets.GetMailbox => Items("""{"DisplayName":"Reception","PrimarySmtpAddress":"reception@contoso.com","Name":"reception"}"""),
            ExoCmdlets.GetMailboxPermission => Items(
                """{"Identity":"reception","User":"FS-Alias","AccessRights":["FullAccess"]}""",
                """{"Identity":"reception","User":"Elsewhere","AccessRights":["FullAccess"]}""",
                """{"Identity":"reception","User":"No Id","AccessRights":["FullAccess"]}""",
                """{"Identity":"reception","User":"S-1-5-21-1","AccessRights":["FullAccess"]}"""),
            _ => Items(),
        });
        await using var backends = new ExchangeBackends(sidecar, null, () => new GraphClient(new ScriptedTransport(_ => new GraphResponse(200, [], CarolsGroups))));

        UserAccess access = (await MailboxLookups.AccessForUserAsync(backends, "carol@contoso.com", new UserAccessOptions(ExpandGroups: true, Method: PermissionReadMethod.Piped), Ct)).Data!;

        UserGrant grant = Assert.Single(access.Grants);
        Assert.Equal("Finance Staff", grant.Via!.DisplayName);
        Assert.Equal(2, access.Groups);
        Assert.Equal(["FS-Alias", "Elsewhere", "No Id"], sidecar.Commands.Last(command => command.Cmdlet == ExoCmdlets.GetRecipient).Input);
    }

    [Fact]
    public async Task AUserWithoutGroupsOrWithoutOtherHoldersHasNoGrantThroughAGroup()
    {
        CannedRunner Sidecar(string holder) => new(command => command.Cmdlet switch
        {
            ExoCmdlets.GetMailbox => Items("""{"DisplayName":"Reception","PrimarySmtpAddress":"reception@contoso.com","Name":"reception"}"""),
            ExoCmdlets.GetMailboxPermission => Items($$"""{"Identity":"reception","User":"{{holder}}","AccessRights":["FullAccess"]}"""),
            _ => Items(),
        });
        await using var noGroups = new ExchangeBackends(Sidecar("Finance Staff"), null, () => new GraphClient(new ScriptedTransport(_ => new GraphResponse(200, [], """{"value":[]}"""))));
        await using var noOthers = new ExchangeBackends(Sidecar("carol@contoso.com"), null, () => new GraphClient(new ScriptedTransport(_ => new GraphResponse(200, [], CarolsGroups))));

        UserAccess withoutGroups = (await MailboxLookups.AccessForUserAsync(noGroups, "carol@contoso.com", new UserAccessOptions(ExpandGroups: true, Method: PermissionReadMethod.Piped), Ct)).Data!;
        UserAccess withoutOthers = (await MailboxLookups.AccessForUserAsync(noOthers, "carol@contoso.com", new UserAccessOptions(ExpandGroups: true, Method: PermissionReadMethod.Piped), Ct)).Data!;

        Assert.Empty(withoutGroups.Grants);
        Assert.Equal(0, withoutGroups.Groups);
        Assert.Null(Assert.Single(withoutOthers.Grants).Via);
    }

    [Fact]
    public async Task TheReportNeedsGraph()
    {
        await using var backends = new ExchangeBackends(new CannedRunner(_ => Items()));

        await Assert.ThrowsAsync<InvalidOperationException>(() => MailboxLookups.SizeAsync(backends, null, null, new MailboxSizeOptions(), Ct));
    }

    [Fact]
    public async Task AReportWhoseNamesAreConcealedCannotBeJoined()
    {
        const string report = "Report Refresh Date,User Principal Name,Is Deleted\n2026-10-29,0123456789ABCDEF0123456789abcdef,False\n";
        await using var backends = new ExchangeBackends(new CannedRunner(_ => Items()), null, () => new GraphClient(new ScriptedTransport(_ => new GraphResponse(200, [], report))));

        AegisException exception = await Assert.ThrowsAsync<AegisException>(() => MailboxLookups.SizeAsync(backends, null, null, new MailboxSizeOptions(), Ct));

        Assert.Equal(FailureKind.ServiceError, exception.Failure.Kind);
        Assert.StartsWith("the mailbox usage report conceals the user names", exception.Failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheReportJoinsTheSidecarsListByAddressAndLogsTheTimeOfEachRead()
    {
        string home = ProfileStoreTests.TempHome();
        const string report = "User Principal Name,Item Count,Storage Used (Byte)\nshared@contoso.com,4,4096\n";
        var sidecar = new CannedRunner(_ => Items(
            """{"DisplayName":"Shared","PrimarySmtpAddress":"shared@contoso.com"}""",
            """{"DisplayName":"Other","PrimarySmtpAddress":"other@contoso.com","UserPrincipalName":"other@contoso.com"}"""));
        await using var backends = new ExchangeBackends(sidecar, null, () => new GraphClient(new ScriptedTransport(_ => new GraphResponse(200, [], report))));

        MailboxSizes sizes;
        using (LogSink.UseVerbose(LogSink.ForHome(home)))
        {
            sizes = (await MailboxLookups.SizeAsync(backends, null, null, new MailboxSizeOptions(), Ct)).Data!;
        }

        MailboxSize size = Assert.Single(sizes.Mailboxes);
        Assert.Equal((4L, 4096L), (size.ItemCount, size.TotalItemBytes!.Value));
        Assert.Equal(1, sizes.NotInReport);
        Assert.Null(sizes.ReportDate);
        string log = ReadLog(home);
        Assert.Contains("mailbox size, report method: Graph's usage report answered after", log, StringComparison.Ordinal);
        Assert.Contains("mailbox size, report method: the sidecar's mailbox list answered after", log, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFailedReadOfTheReportIsLoggedAsFailed()
    {
        string home = ProfileStoreTests.TempHome();
        var admin = new CannedRunner(_ => Items("""{"DisplayName":"Shared","PrimarySmtpAddress":"shared@contoso.com"}"""));
        await using var backends = new ExchangeBackends(new CannedRunner(_ => Items()), admin, () => new GraphClient(new ScriptedTransport(_ => new GraphResponse(500, [], ""))));

        using (LogSink.UseVerbose(LogSink.ForHome(home)))
        {
            await Assert.ThrowsAsync<AegisException>(() => MailboxLookups.SizeAsync(backends, "shared", null, new MailboxSizeOptions(MailboxSizeMethod.Report), Ct));
        }

        Assert.Contains("mailbox size, report method: Graph's usage report failed after", ReadLog(home), StringComparison.Ordinal);
        Assert.Equal(AdminApiCmdlets.GetMailbox, Assert.Single(admin.Commands).Cmdlet);
    }

    [Fact]
    public void AnEmptyReportHasNoRowsAndAReportWithoutTheNameColumnIsAServiceError()
    {
        Assert.Empty(MailboxLookups.UsageReport(""));
        Assert.Empty(MailboxLookups.UsageReport("\n,,\n"));
        Assert.Equal(FailureKind.ServiceError, Assert.Throws<AegisException>(() => MailboxLookups.UsageReport("Item Count\n4\n")).Failure.Kind);
    }

    [Theory]
    [InlineData("User Principal Name,Item Count\r\n\"a,b@contoso.com\",\"1\"\"2\"\r\nc@contoso.com,3", "a,b@contoso.com|c@contoso.com")]
    [InlineData("User Principal Name,Item Count\nc@contoso.com,", "c@contoso.com")]
    [InlineData("User Principal Name\n\"multi\nline@contoso.com\"\n", "multi\nline@contoso.com")]
    public void AReportsFieldsMayBeQuotedAndItsLastLineNeedsNoBreak(string csv, string names) =>
        Assert.Equal(names.Split('|'), MailboxLookups.UsageReport(csv).Select(row => row.UserPrincipalName));

    private const string CarolsGroups = """
        {"value":[
          {"id":"00000000-0000-0000-0000-000000000035","displayName":"Finance Staff","mail":"finance-staff@contoso.com","mailNickname":"finance-staff"},
          {"id":"00000000-0000-0000-0000-000000000031","displayName":"Finance"}]}
        """;

    internal static ExoObject Object(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return new ExoObject(document.RootElement);
    }

    internal static ExoResult Items(params string[] items) => new([.. items.Select(Object)], []);

    private static string ReadLog(string home) =>
        File.ReadAllText(Assert.Single(Directory.GetFiles(Path.Combine(home, LogSink.FolderName))));
}

/// <summary>An Exchange Online layer whose answers a test computes from the command; every command is recorded.</summary>
internal sealed class CannedRunner(Func<ExoCommand, ExoResult> answer) : IExoCmdletRunner
{
    private readonly Lock _gate = new();

    internal List<ExoCommand> Commands { get; } = [];

    public async Task<ExoResult> InvokeAsync(ExoCommand command, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            Commands.Add(command);
        }

        await Task.Yield();
        return answer(command);
    }
}
