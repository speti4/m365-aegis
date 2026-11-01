using System.Text.Json;
using M365Aegis.Cli.Commands;
using M365Aegis.Cli.Output;
using M365Aegis.Core.Audit;
using M365Aegis.Core.Auth;
using M365Aegis.Core.Config;
using M365Aegis.Core.Diagnostics;
using M365Aegis.Core.Exchange;
using M365Aegis.Core.Graph;
using M365Aegis.Core.Groups;
using M365Aegis.Core.Operations;
using M365Aegis.Core.Output;
using M365Aegis.Core.Snapshots;

namespace M365Aegis.Cli.Tests;

/// <summary>
/// The table views in process, over made-up results shaped to reach the cases the end-to-end tests do not: a missing
/// name, an empty list, a value no enum member names (task 070). The snapshot tests keep the full renderings.
/// </summary>
public sealed class TableViewTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);
    private static readonly MailboxInfo Reception = new("Reception", "reception@contoso.com", "SharedMailbox", null, "00000000-0000-0000-0000-000000000040");
    private static readonly GroupInfo Finance = new("00000000-0000-0000-0000-000000000030", "Finance", GroupKind.Distribution, "finance@contoso.com", Synced: false);
    private static readonly GroupMember Bob = new("user", null, null, "bob@contoso.com", "00000000-0000-0000-0000-000000000003");
    private static readonly GroupMember Carol = new("user", "Carol", null, null, "00000000-0000-0000-0000-000000000004", Nested: true)
    {
        Via = [new MemberGroup("00000000-0000-0000-0000-000000000031", "Finance EU")],
    };

    [Fact]
    public void ASignInWithoutANameATenantOrAnObjectAndWithEveryKindOfToken()
    {
        var info = new SignInInfo("work", "alice@contoso.com", null, null, null,
        [
            new ResourceToken(Resource.Graph, Now, [], TokenOrigin.Cache, null),
            new ResourceToken(Resource.Exchange, Now, ["Exchange.Manage"], TokenOrigin.IdentityProvider, null),
            new ResourceToken((Resource)99, null, [], null, null),
        ]);

        string table = Show(SignInTableView.Instance, info);

        Assert.DoesNotContain("name|", table, StringComparison.Ordinal);
        Assert.Contains("tenant|-", table, StringComparison.Ordinal);
        Assert.Contains("object id|-", table, StringComparison.Ordinal);
        Assert.Contains("from the cache", table, StringComparison.Ordinal);
        Assert.Contains("from the identity provider", table, StringComparison.Ordinal);
        Assert.Contains("graph scopes|-", table, StringComparison.Ordinal);
        Assert.Contains("? token|none (no reason given)", table, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0, "nothing was signed in; the profile stays")]
    [InlineData(2, "signed out; the profile stays")]
    public void ASignOutSaysWhatItRemoved(int removed, string footer) =>
        Assert.EndsWith(footer, Show(SignOutTableView.Instance, new SignOutInfo("work", "alice@contoso.com", removed)), StringComparison.Ordinal);

    [Fact]
    public void ADoctorCheckOfAnUnknownStatusIsAQuestionMark()
    {
        var report = new DoctorReport("home", [new DoctorCheck("odd", (CheckStatus)99, "detail", Required: false)]);

        Assert.Contains("odd|?|detail", Show(DoctorCommand.DoctorTableView.Instance, report), StringComparison.Ordinal);
    }

    [Fact]
    public void AnAuditListOfAnActivityAndAUser()
    {
        var report = new DirectoryAuditReport(Now.AddDays(-1), "Add member", [], Truncated: false, User: "alice@contoso.com");

        Assert.Contains("0 entry(ies) starting with 'Add member' initiated by alice@contoso.com since", Show(AuditListTableView.Instance, report), StringComparison.Ordinal);
    }

    [Fact]
    public void AnAuditListWithoutAnActivityThatWasCutAtTheOldestUnknown()
    {
        var report = new DirectoryAuditReport(Now.AddDays(-1), null, [], Truncated: true);

        string table = Show(AuditListTableView.Instance, report);

        Assert.Contains("0 entry(ies) since", table, StringComparison.Ordinal);
        Assert.Contains("more entry match: --limit raises the cap", table, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true, "write the answer to the file")]
    [InlineData(false, "the file is longer than the")]
    public void AFixtureNameIsAFieldPerRowAndSaysWhetherItFits(bool fits, string footer)
    {
        var name = new FixtureName("https://graph.microsoft.com/v1.0/me", "GET v1.0/me", "GET v1.0/me", "tests/fixtures/graph/me.json", 30, fits);

        string table = Show(FixtureNameTableView.Instance, name);

        Assert.Contains("field|value", table, StringComparison.Ordinal);
        Assert.Contains("length|30", table, StringComparison.Ordinal);
        Assert.Contains(footer, table, StringComparison.Ordinal);
    }

    [Fact]
    public void ACallWithoutAnAnswerHasNoTable()
    {
        Assert.Equal([], CallTableView.Instance.HeadersFor(null));
        Assert.Empty(CallTableView.Instance.Rows(null));
        Assert.Empty(CallTableView.Instance.Footer(null));
    }

    [Theory]
    [InlineData("[]", "value", "0 item(s)")]
    [InlineData("[1, \"two\", null]", "value", "3 item(s)")]
    [InlineData("""{"value":[{"nested":{"a":1}}]}""", "value", "1 item(s)")]
    [InlineData("""{"value":"not a list","@odata.nextLink":"https://graph.microsoft.com/v1.0/next"}""", "field|value", "more pages follow")]
    [InlineData("\"a plain text\"", "value", null)]
    [InlineData("null", "value", null)]
    public void ACallAnswerOfAnyShapeIsATable(string json, string headers, string? footer)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement? answer = document.RootElement;

        string table = Show(CallTableView.Instance, answer);

        Assert.StartsWith(headers, table, StringComparison.Ordinal);
        if (footer is null)
        {
            Assert.Empty(CallTableView.Instance.Footer(answer));
        }
        else
        {
            Assert.Contains(footer, table, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ACollectionOfMixedItemsAndLongCells()
    {
        string longText = new('x', 100);
        using JsonDocument document = JsonDocument.Parse($$$"""
            {"value":[
              {"a":"1","b":null,"c":2,"d":true,"e":"{{{longText}}}","f":"line one\nline two","g":"seventh","h":{"skip":1}},
              "a plain item"
            ]}
            """);

        string table = Show(CallTableView.Instance, document.RootElement);

        Assert.StartsWith("a|b|c|d|e|f", table, StringComparison.Ordinal);
        Assert.Contains("1|-|2|true|" + new string('x', 79) + "…|line one line two", table, StringComparison.Ordinal);
        Assert.Contains("|||||", table, StringComparison.Ordinal);
        Assert.Contains("2 item(s); the first 6 of 7 fields, --json for all", table, StringComparison.Ordinal);
    }

    [Fact]
    public void AGroupMemberWithoutANameOrAnAddressAndOneThroughANestedGroup()
    {
        var members = new GroupMembers(Finance, [Bob, Carol, Carol with { Via = [] }, Bob with { Mail = null }], Transitive: true) { Via = true };

        string table = Show(GroupMembersTableView.Instance, members);

        Assert.Contains("user|-|bob@contoso.com|direct|-|", table, StringComparison.Ordinal);
        Assert.Contains("user|Carol|-|nested|Finance EU|", table, StringComparison.Ordinal);
        Assert.Contains("user|Carol|-|nested|-|", table, StringComparison.Ordinal);
        Assert.Contains("user|-|-|direct|-|", table, StringComparison.Ordinal);
        Assert.Contains("user|-|-|", Show(GroupOwnersTableView.Instance, new GroupOwners(Finance, [Bob with { Mail = null }])), StringComparison.Ordinal);
    }

    [Fact]
    public void ATransitiveListWithoutTheViaColumn()
    {
        string table = Show(GroupMembersTableView.Instance, new GroupMembers(Finance, [Bob, Carol], Transitive: true));

        Assert.Contains("user|-|bob@contoso.com|direct|", table, StringComparison.Ordinal);
        Assert.Contains("user|Carol|-|nested|", table, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryGroupKindHasItsNameAndAnUnknownOneAQuestionMark()
    {
        Assert.Equal("Microsoft 365", GroupCommand.Kind(GroupKind.Microsoft365));
        Assert.Equal("security", GroupCommand.Kind(GroupKind.Security));
        Assert.Equal("mail-enabled security", GroupCommand.Kind(GroupKind.MailEnabledSecurity));
        Assert.Equal("distribution", GroupCommand.Kind(GroupKind.Distribution));
        Assert.Equal("?", GroupCommand.Kind((GroupKind)99));
        Assert.Equal("Finance (distribution, synced, 00000000-0000-0000-0000-000000000030)", GroupCommand.Describe(Finance with { Synced = true }));
    }

    [Fact]
    public void TheOwnersOfAGroupGraphKeepsOwnersFor()
    {
        string table = Show(GroupOwnersTableView.Instance, new GroupOwners(Finance with { Kind = GroupKind.Microsoft365 }, [Bob]));

        Assert.Contains("1 owner(s) of Finance", table, StringComparison.Ordinal);
        Assert.DoesNotContain("Graph keeps no owners", table, StringComparison.Ordinal);
        Assert.Contains("Graph keeps no owners", Show(GroupOwnersTableView.Instance, new GroupOwners(Finance, [])), StringComparison.Ordinal);
        // The ManagedBy of task 076 answered: no footer about Graph.
        Assert.DoesNotContain("Graph keeps no owners", Show(GroupOwnersTableView.Instance, new GroupOwners(Finance, [Bob]) { FromExchange = true }), StringComparison.Ordinal);
    }

    [Fact]
    public void AnOwnerlessListWithNothingLeftOut()
    {
        string table = Show(OwnerlessGroupsTableView.Instance, new OwnerlessGroups([Finance with { Mail = null, Synced = true }], LeftOut: 0));

        Assert.Contains("Finance|distribution, synced|-|", table, StringComparison.Ordinal);
        Assert.DoesNotContain("left out", table, StringComparison.Ordinal);
        Assert.Contains("1 synced group(s) as Graph lists them", table, StringComparison.Ordinal);
    }

    [Fact]
    public void AnOwnerlessListExchangeJudged()
    {
        // Task 089: two groups left out by their ManagedBy, none synced.
        string table = Show(OwnerlessGroupsTableView.Instance, new OwnerlessGroups([Finance], LeftOut: 0) { ManagedInExchange = 2 });
        string none = Show(OwnerlessGroupsTableView.Instance, new OwnerlessGroups([Finance], LeftOut: 0) { ManagedInExchange = 0 });

        Assert.Contains("2 distribution or mail-enabled security group(s) left out: their ManagedBy in Exchange Online names an owner", table, StringComparison.Ordinal);
        Assert.DoesNotContain("synced", table, StringComparison.Ordinal);
        Assert.DoesNotContain("left out", none, StringComparison.Ordinal);
    }

    [Fact]
    public void APlanListWithEveryModeAndNoRecord()
    {
        PlanRecordSummary Record(PlanMode mode) => new($"record-{mode}", "group-members-add", mode, "title", 1, 0, 0, 0);
        var list = new PlanRecordList("plans", [.. Enum.GetValues<PlanMode>().Select(Record), Record((PlanMode)99)]);

        string table = Show(PlanListTableView.Instance, list);

        Assert.Contains("|cut short|", table, StringComparison.Ordinal);
        Assert.Contains("|refused|", table, StringComparison.Ordinal);
        Assert.Contains("|?|", table, StringComparison.Ordinal);
        Assert.Contains("-|group-members-add", table, StringComparison.Ordinal);
        Assert.DoesNotContain("plan show", Show(PlanListTableView.Instance, new PlanRecordList("plans", [])), StringComparison.Ordinal);
        Assert.Contains("2 file(s) there are not plan records", Show(PlanListTableView.Instance, new PlanRecordList("plans", []) { Unreadable = 2 }), StringComparison.Ordinal);
    }

    [Fact]
    public void APlanRecordWithoutAProfileArgumentsOrAFileKeepsThePlansHeaders()
    {
        var plan = new Plan("group-members-add", "add to Finance", [], [], []) { Mode = PlanMode.UpToDate };
        var record = new PlanRecord(1, "m365-aegis", null, [], plan);

        string table = Show(PlanRecordTableView.Instance, record);

        Assert.Equal(PlanTableView.Instance.Headers, PlanRecordTableView.Instance.Headers);
        Assert.Contains("profile (the default one)", table, StringComparison.Ordinal);
        Assert.DoesNotContain("arguments:", table, StringComparison.Ordinal);
        Assert.DoesNotContain("from the file", table, StringComparison.Ordinal);
    }

    [Fact]
    public void APlanWithEveryActionAndResult()
    {
        PlanChange Change(PlanAction action, ChangeResult? result) => new(action, "Finance", "member bob", "-", "member") { Result = result };
        var plan = new Plan(
            "group-members-add",
            "add to Finance",
            [
                Change(PlanAction.Add, new ChangeResult(ChangeStatus.Applied)),
                Change(PlanAction.Remove, new ChangeResult(ChangeStatus.Failed, new EnvelopeError("forbidden", "denied"))),
                Change(PlanAction.Set, new ChangeResult(ChangeStatus.Failed)),
                Change(PlanAction.Clear, new ChangeResult(ChangeStatus.Skipped)),
                Change(PlanAction.Send, new ChangeResult((ChangeStatus)99)),
                Change((PlanAction)99, null),
            ],
            [new PlanUnchanged("Finance", "member carol", "member") { Row = 2 }],
            ["a warning"])
        {
            Mode = PlanMode.Applied,
            Record = "record-1",
        };

        string table = Show(PlanTableView.Instance, plan);

        Assert.StartsWith("line|action", table, StringComparison.Ordinal);
        Assert.Contains("-|add|Finance|member bob|-|member|applied", table, StringComparison.Ordinal);
        Assert.Contains("|failed: forbidden", table, StringComparison.Ordinal);
        Assert.Contains("|clear|", table, StringComparison.Ordinal);
        Assert.Contains("|send|Finance|member bob|-|member|no result", table, StringComparison.Ordinal);
        Assert.Contains("-|?|Finance|member bob|-|member|no result", table, StringComparison.Ordinal);
        Assert.Contains("2|-|Finance|member carol|member|-|unchanged", table, StringComparison.Ordinal);
        Assert.Contains("failed (Finance, member bob): denied (forbidden)", table, StringComparison.Ordinal);
        Assert.Contains("failed (Finance, member bob):  ()", table, StringComparison.Ordinal);
        Assert.Contains("applied 1 of 6 change(s); the plan record is record-1", table, StringComparison.Ordinal);
    }

    [Fact]
    public void APlanCutShortNamesItsRecord()
    {
        var plan = new Plan("group-members-add", "add to Finance", [], [], []) { Mode = PlanMode.Applying, Record = "record-2", Resumes = Path.Combine("plans", "record-1.json") };

        string table = Show(PlanTableView.Instance, plan);

        Assert.Contains("resumes: record-1.json (its failed and skipped changes, planned again)", table, StringComparison.Ordinal);
        Assert.EndsWith("the apply did not finish; the plan record is record-2", table, StringComparison.Ordinal);
    }

    [Fact]
    public void ARowOfAnotherWidthThanTheHeadersIsRefused() =>
        Assert.Throws<ArgumentException>(() => TableWriter.Render(["a", "b"], [["only one"]]));

    [Fact]
    public void ANoTableViewHasNothingToShow()
    {
        Assert.Empty(NoTableView<int>.Instance.Headers);
        Assert.Empty(NoTableView<int>.Instance.Rows(1));
    }

    [Fact]
    public void AFailureWithoutAHintIsOneErrorLine()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        ExitCode exitCode = Renderer.Render(
            CommandResult.Failure<object>(new AegisFailure(FailureKind.NotFound, "nothing there")),
            new EnvelopeMeta([], 1, Backend.Local),
            NoTableView<object>.Instance,
            OutputMode.Table,
            output,
            error);

        Assert.Equal(ExitCode.NotFound, exitCode);
        Assert.Equal("error: nothing there (not-found)\n", error.ToString());
    }

    [Fact]
    public void AProfileListThatIsEmptyOrHasNoDefault()
    {
        Assert.Contains("no profile yet:", Show(ProfileCommand.ProfileListTableView.Instance, new ProfileList("config.json", null, [])), StringComparison.Ordinal);
        var two = new ProfileList("config.json", null,
        [
            new ProfileSummary("work", "contoso.onmicrosoft.com", "00000000-0000-0000-0000-000000000010", "alice@contoso.com", IsDefault: false),
        ]);
        Assert.Contains("no default:", Show(ProfileCommand.ProfileListTableView.Instance, two), StringComparison.Ordinal);
    }

    [Fact]
    public void AProfileRemovalThatKeptTheSignInAndLeftNoDefault()
    {
        string table = Show(ProfileCommand.ProfileRemovalTableView.Instance, new ProfileRemoval("lab", "alice@contoso.com", SignedOut: false, RemovedAccounts: 0, Default: null));

        Assert.Contains("signed out|no: another profile", table, StringComparison.Ordinal);
        Assert.Contains("default|-", table, StringComparison.Ordinal);
    }

    [Fact]
    public void ExpiringSecretsWithoutTheExpiredOnesAndACertificateOfAServicePrincipal()
    {
        var credential = new ExpiringCredential(CredentialOwner.ServicePrincipal, "App", null, "00000000-0000-0000-0000-000000000050", CredentialKind.Certificate, null, "key-1", Now, 3, Expired: false);

        string table = Show(SecretsExpiringTableView.Instance, new CredentialExpiryReport(30, Now, IncludeExpired: false, 1, 1, [credential]));

        Assert.Contains("|3|App|service principal|certificate|key-1", table, StringComparison.Ordinal);
        Assert.Contains("1 credential(s) expire within 30 day(s); 1 app", table, StringComparison.Ordinal);
    }

    [Fact]
    public void SignInsOfAUserKindAreUserColumns()
    {
        var report = new SignInReport(null, FailuresOnly: false, Now.AddDays(-1), [], Truncated: false, Kind: SignInKind.Interactive);

        Assert.Equal(SignInListTableView.Instance.Headers, SignInListTableView.Instance.HeadersFor(report));
    }

    [Fact]
    public void ATraceFilteredByStatusOnlyOrBySubjectOnly()
    {
        var byStatus = new MessageTrace(Now, Now, [], Status: "Failed", FilteredBy: "service");
        var bySubject = new MessageTrace(Now, Now, [], Subject: "Invoice", FilteredBy: "here");

        Assert.Contains("filtered by the service: status Failed", Show(TraceTableView.Instance, byStatus), StringComparison.Ordinal);
        Assert.Contains("filtered here: subject containing 'Invoice'", Show(TraceTableView.Instance, bySubject), StringComparison.Ordinal);
    }

    [Fact]
    public void TraceDetailsWithoutAMessageIdOrRecipients()
    {
        var recipient = new MessageTraceRecipient("00000000-0000-0000-0000-000000000060", "bob@contoso.com", null, [new MessageTraceEvent(Now, null, null, null, null)]);

        Assert.Contains("1 step(s) of 00000000-0000-0000-0000-000000000060 to 1", Show(TraceDetailsTableView.Instance, new MessageTraceDetails(null, null, [recipient])), StringComparison.Ordinal);
        Assert.Contains("0 step(s) of - to 0", Show(TraceDetailsTableView.Instance, new MessageTraceDetails(null, null, [])), StringComparison.Ordinal);
    }

    [Fact]
    public void MailboxHelpersCoverEveryValue()
    {
        Assert.Equal("?", MailboxCommand.Right((MailboxRight)99));
        Assert.Equal("?", MailboxCommand.Size(null));
        Assert.Equal("Finance > Finance EU", MailboxCommand.Through("Finance", Carol));
        Assert.Equal("Finance", MailboxCommand.Through("Finance", Bob));
        Assert.Equal("Finance", MailboxCommand.Through("Finance", Carol with { Via = [] }));
    }

    [Fact]
    public void AnExpandedAccessListWithAGroupThatCouldNotBeExpanded()
    {
        var access = new MailboxAccess(Reception,
        [
            new MailboxHolder(MailboxRight.FullAccess, "Finance", "finance@contoso.com") { Group = true, Members = [Bob] },
            new MailboxHolder(MailboxRight.SendAs, "All staff", null) { Group = true },
        ]);

        Assert.Contains("1 group(s) without a directory object", Show(MailboxAccessTableView.Instance, access), StringComparison.Ordinal);
    }

    [Fact]
    public void AUserAccessListWithAndWithoutAName()
    {
        var grant = new UserGrant(MailboxRight.FullAccess, Reception);
        var direct = new UserAccess("bob@contoso.com", "Bob", [grant], 3, []);
        var expanded = new UserAccess("bob@contoso.com", null, [grant], 3, ["a list"]) { Groups = 2 };

        Assert.Contains("for bob@contoso.com (Bob) over 3", Show(UserAccessTableView.Instance, direct), StringComparison.Ordinal);
        Assert.Contains("for bob@contoso.com over 3", Show(UserAccessTableView.Instance, direct with { DisplayName = null }), StringComparison.Ordinal);
        Assert.Contains("for bob@contoso.com (Bob) over 3", Show(UserAccessTableView.Instance, expanded with { DisplayName = "Bob" }), StringComparison.Ordinal);
        string table = Show(UserAccessTableView.Instance, expanded);
        Assert.Contains("for bob@contoso.com over 3", table, StringComparison.Ordinal);
        Assert.Contains("1 list(s) could not be read", table, StringComparison.Ordinal);
    }

    [Fact]
    public void MailboxSizesFromAReportWithoutADateAndLiveInParallel()
    {
        var size = new MailboxSize(Reception, 1, null, null, 0, null);
        var report = new MailboxSizes([size], 2, ["one"]) { Method = "report" };

        string fromReport = Show(MailboxSizeTableView.Instance, report);
        Assert.Contains("usage report of ?; 0 mailbox(es) not in the report yet", fromReport, StringComparison.Ordinal);
        Assert.Contains("1 mailbox(es) could not be read", fromReport, StringComparison.Ordinal);
        Assert.Contains("read live with --method parallel", Show(MailboxSizeTableView.Instance, report with { Method = "parallel", Failed = [] }), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("calendar", "")]
    [InlineData("Kalender", " (Kalender)")]
    public void AFolderNameIsShownWhenItDiffersFromTheFolder(string? name, string shown) =>
        Assert.Contains($"on the folder Calendar{shown} of reception", Show(FolderPermissionsTableView.Instance, new MailboxFolderPermissions("reception", "Calendar", name, [])), StringComparison.Ordinal);

    [Fact]
    public void AnOrganizationWithNothingSet()
    {
        var organization = new ExchangeOrganization(null, null, false, null, [new AcceptedDomainInfo("contoso.com", null, null)]);

        string table = Show(OrganizationTableView.Instance, organization);

        Assert.Contains("name|-", table, StringComparison.Ordinal);
        Assert.Contains("MailTips|-", table, StringComparison.Ordinal);
        Assert.Contains("MailTip for external recipients|off", table, StringComparison.Ordinal);
        Assert.Contains("MailTip for a large audience from|-", table, StringComparison.Ordinal);
        Assert.Contains("accepted domain|contoso.com (?)", table, StringComparison.Ordinal);
    }

    [Fact]
    public void AConnectMeasurementWithNothingKnownAndAnAdminApiProbeOfAMailbox()
    {
        var environment = new MeasureEnvironment(null, null, null, false, 0, false, [], []);
        var empty = new Spread(null, null, null);
        var report = new ConnectMeasurementReport(environment, [new VariantSummary(ConnectVariant.Default, 1, 1, empty, empty, empty, empty, empty, false)], []);

        string measured = Show(ConnectMeasurementTableView.Instance, report);
        Assert.Contains("pwsh ?, ExchangeOnlineManagement ?", measured, StringComparison.Ordinal);
        Assert.Contains("the module ?, the first PSModulePath entry no", measured, StringComparison.Ordinal);
        Assert.Contains("median (spread) total -", measured, StringComparison.Ordinal);

        var call = new AdminApiCall("Mailbox (forwarding)", "upn", null, 5, null, [], null, null, null);
        var answered = new AdminApiCall("Mailbox (select)", "upn", 200, 5, 1, ["value[0].ForwardingSmtpAddress: string"], null, null, null);
        string probed = Show(AdminApiProbeTableView.Instance, new AdminApiProbeReport(false, [], [call, answered]));
        Assert.Contains("Mailbox (forwarding): status -, forwarding fields none", probed, StringComparison.Ordinal);
        Assert.Contains("Mailbox (select): status 200, forwarding fields ForwardingSmtpAddress", probed, StringComparison.Ordinal);

        var oneDrive = new MeasureEnvironment("7.6.9", "3.10.1", true, null, 1, true, [], []);
        Assert.Contains("the module yes, the first PSModulePath entry ?", Show(ConnectMeasurementTableView.Instance, report with { Environment = oneDrive }), StringComparison.Ordinal);

        var connected = new ConnectProbeReport("organization", "7.6.9", null, 1, 2, 3, 4, 5, 6, 59.5, ["Exchange.Manage"]);
        string connect = Show(ConnectProbeTableView.Instance, connected);
        Assert.Contains("module|-", connect, StringComparison.Ordinal);
        Assert.Contains("Manage and ManageV2 on one token|no", connect, StringComparison.Ordinal);
    }

    /// <summary>The headers, the rows and the footer of a view, cells joined by a bar, one line each.</summary>
    [Fact]
    public void ASnapshotTakeThatPrunedThatFailedOrThatLeftFilesBehind()
    {
        var domain = new SnapshotDomain("users", SnapshotDomainStatus.Ok, "graph /users", 3);
        var sharing = new SnapshotDomain("sharing", SnapshotDomainStatus.Ok, "graph", 9) { ElapsedMs = 61_240, GraphRequests = 40, ThrottledMs = 3_500 };
        var calendars = new SnapshotDomain("folderPermissions", SnapshotDomainStatus.Ok, "admin-api", 2) { ElapsedMs = 800, GraphRequests = 2, AdminApiCalls = 6, SidecarCalls = 1 };
        var info = new SnapshotInfo("2026-10-30T120000Z.sqlite", Now, 2048, []);
        var take = new SnapshotTakeResult(info, Path.Combine("home", "snapshots", "work", info.Name), 1500, [sharing, calendars, domain], new SnapshotRetention(), ["2026-09-01T000000Z.sqlite"])
        {
            RemovedPartials = ["2026-10-29T000000Z.sqlite.partial"],
            Warnings = ["2026-08-01T000000Z.sqlite could not be pruned (in use); the next take tries again"],
        };

        string pruned = Show(SnapshotTakeTableView.Instance, take);
        string failed = Show(SnapshotTakeTableView.Instance, take with { Snapshot = info with { Failed = ["grants"] }, Pruned = [] });

        Assert.StartsWith("domain|status|rows|time|requests|note\nsharing|ok|9|61.2 s|40 graph (throttled 3.5 s)|\nfolderPermissions|ok|2|0.8 s|2 graph, 6 admin-api, 1 pwsh|\n", pruned, StringComparison.Ordinal);
        Assert.Contains("users|ok|3|0.0 s|-|\nsnapshot 2026-10-30T120000Z.sqlite (2.0 KB) in ", pruned, StringComparison.Ordinal);
        Assert.Contains(", read in 1.5 s\npruned 1 by the retention (14 days, 8 weeks): 2026-09-01T000000Z.sqlite", pruned, StringComparison.Ordinal);
        Assert.Contains("removed 2026-10-29T000000Z.sqlite.partial, left by a take that did not finish", pruned, StringComparison.Ordinal);
        Assert.EndsWith("warning: 2026-08-01T000000Z.sqlite could not be pruned (in use); the next take tries again", pruned, StringComparison.Ordinal);
        Assert.Contains("nothing pruned: a domain failed, so the older snapshots are kept", failed, StringComparison.Ordinal);
    }

    [Fact]
    public void ASnapshotListWithFailuresAndUnreadableFiles()
    {
        var list = new SnapshotList("work", "folder", [new SnapshotInfo("2026-10-30T120000Z.sqlite", Now, 10, ["grants", "sharing"])]) { Unreadable = 2 };

        string table = Show(SnapshotListTableView.Instance, list);

        Assert.Contains("2026-10-30 12:00:00|2026-10-30T120000Z.sqlite|10 B|grants, sharing", table, StringComparison.Ordinal);
        Assert.Contains("2 file(s) named like a snapshot cannot be read as one and are left out", table, StringComparison.Ordinal);
    }

    [Fact]
    public void AnAccessViewWithoutNamesAndWithEveryKindOfRow()
    {
        var report = new AccessReport(
            "2026-10-30T120000Z.sqlite",
            Now,
            50,
            new AccessUser(null, null, null, "partner@fabrikam.com"),
            [new AccessGroup(null, null, null, []), new AccessGroup("id", "Finance", "Security", ["Finance EU", "Staff"])],
            [new AccessSite("Finance", null, null, "owner")],
            [new AccessSharedItem("Finance", "Budget.xlsx", null, "link", "anonymous", "read", "2026-12-31T00:00:00Z", null)],
            [new AccessMailbox(null, "x@contoso.com", "send-as", "Finance"), new AccessMailbox("Box", "box@contoso.com", "full-access", null)],
            [new AccessCalendar(null, "x@contoso.com", "Calendar", "Reviewer", null)],
            new AccessEveryone(0, 0, 0, 0),
            ["a warning"]);

        string table = Show(AccessTableView.Instance, report);

        Assert.Contains("group|?|?|direct\ngroup|Finance|Security|Finance EU, Staff", table, StringComparison.Ordinal);
        Assert.Contains("site|Finance|-|owner of ?", table, StringComparison.Ordinal);
        Assert.Contains("shared|Finance: /Budget.xlsx|link anonymous read until 2026-12-31T00:00:00Z|direct", table, StringComparison.Ordinal);
        Assert.Contains("mailbox|? <x@contoso.com>|send-as|Finance\nmailbox|Box <box@contoso.com>|full-access|direct", table, StringComparison.Ordinal);
        Assert.Contains("calendar|? <x@contoso.com>|Calendar: Reviewer|direct", table, StringComparison.Ordinal);
        Assert.Contains("? (partner@fabrikam.com) from the snapshot 2026-10-30T120000Z.sqlite, taken 2026-10-30 12:00:00 UTC, 50 hours ago", table, StringComparison.Ordinal);
        Assert.EndsWith("warning: a warning", table, StringComparison.Ordinal);
        Assert.Contains("? (00000000-0000-0000-0000-000000000001) from", Show(AccessTableView.Instance, report with { User = new AccessUser("00000000-0000-0000-0000-000000000001", null, null, null) }), StringComparison.Ordinal);
    }

    [Fact]
    public void ASnapshotDiffWithADomainLeftOutCountsOnlyTheComparedOnes()
    {
        var diff = new SnapshotDiffReport(
            new SnapshotDiffSide("2026-10-29T120000Z.sqlite", Now.AddDays(-1), 24, "yesterday"),
            new SnapshotDiffSide("2026-10-30T120000Z.sqlite", Now, 0.5, "latest"),
            [new SnapshotDiffDomain("members", true, 1, 0), new SnapshotDiffDomain("grants", false, 0, 0)],
            [new SnapshotDiffEntry("members", SnapshotChange.Added, "Finance", "bob@contoso.com", "member")],
            ["grants is left out: grants was not read in 2026-10-30T120000Z.sqlite"]);

        string table = Show(SnapshotDiffTableView.Instance, diff);
        string none = Show(SnapshotDiffTableView.Instance, diff with { Changes = [] });

        Assert.Contains("members|added|Finance|bob@contoso.com|member", table, StringComparison.Ordinal);
        Assert.Contains("from 2026-10-29T120000Z.sqlite (taken 2026-10-29 12:00:00 UTC, 24 hours ago) to 2026-10-30T120000Z.sqlite (taken 2026-10-30 12:00:00 UTC, 0.5 hours ago)", table, StringComparison.Ordinal);
        Assert.Contains("\n1 added, 0 removed: members +1 -0\n", table, StringComparison.Ordinal);
        Assert.EndsWith("warning: grants is left out: grants was not read in 2026-10-30T120000Z.sqlite", table, StringComparison.Ordinal);
        Assert.Contains("\nno change in members\n", none, StringComparison.Ordinal);
    }

    [Fact]
    public void AUserDiffHasTheThroughColumnAndNamesTheUser()
    {
        var diff = new SnapshotDiffReport(
            new SnapshotDiffSide("2026-10-29T120000Z.sqlite", Now.AddDays(-1), 24, "yesterday"),
            new SnapshotDiffSide("2026-10-30T120000Z.sqlite", Now, 0.5, "latest"),
            [new SnapshotDiffDomain("groups", true, 2, 0)],
            [
                new SnapshotDiffEntry("groups", SnapshotChange.Added, "Finance", "bob@contoso.com", "member"),
                new SnapshotDiffEntry("groups", SnapshotChange.Added, "Finance Staff", "bob@contoso.com", "member") { Through = "Audit" },
            ],
            [])
        {
            User = new AccessUser(null, "Partner Example", null, "partner@fabrikam.com"),
        };

        string table = Show(SnapshotDiffTableView.Instance, diff);
        string byId = Show(SnapshotDiffTableView.Instance, diff with { User = new AccessUser("00000000-0000-0000-0000-000000000003", null, null, null) });

        Assert.StartsWith("section|change|what|who|how|through\ngroups|added|Finance|bob@contoso.com|member|direct\ngroups|added|Finance Staff|bob@contoso.com|member|Audit\n", table, StringComparison.Ordinal);
        Assert.Contains("\nfor partner@fabrikam.com (Partner Example), nested memberships and what comes through them included\nfrom ", table, StringComparison.Ordinal);
        Assert.Contains("\nfor 00000000-0000-0000-0000-000000000003, nested memberships", byId, StringComparison.Ordinal);
    }

    private static string Show<T>(ITableView<T> view, T data)
    {
        var lines = new List<string> { string.Join("|", view.HeadersFor(data)) };
        lines.AddRange(view.Rows(data).Select(row => string.Join("|", row)));
        lines.AddRange(view.Footer(data));
        return string.Join("\n", lines);
    }
}
