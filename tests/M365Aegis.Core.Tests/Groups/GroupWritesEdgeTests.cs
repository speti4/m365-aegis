using System.Text.Json;
using System.Text.Json.Nodes;
using M365Aegis.Core.Diagnostics;
using M365Aegis.Core.Exchange;
using M365Aegis.Core.Exchange.Sidecar;
using M365Aegis.Core.Graph;
using M365Aegis.Core.Groups;
using M365Aegis.Core.Operations;
using M365Aegis.Core.Output;
using M365Aegis.Core.Tests.Graph;
using M365Aegis.Tests;

namespace M365Aegis.Core.Tests.Groups;

/// <summary>
/// The group writes over a scripted directory (a distribution group without an address, a synced group, a
/// Microsoft 365 group with two owners) and a scripted Exchange: the Admin API's members of every recipient shape,
/// its fallback to Graph, the warnings of every kind of group, the names a change gives a member without one.
/// </summary>
public sealed class GroupWritesEdgeTests
{
    private const string Sales = "00000000-0000-0000-0000-000000000061";
    private const string Synced = "00000000-0000-0000-0000-000000000062";
    private const string Project = "00000000-0000-0000-0000-000000000063";
    private const string Alice = "00000000-0000-0000-0000-000000000064";
    private const string Bob = "00000000-0000-0000-0000-000000000065";
    private const string Device = "00000000-0000-0000-0000-000000000066";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task NoUserOrNoRowIsAUsageError()
    {
        AegisException noUser = Assert.Throws<AegisException>(() => GroupWrites.FromArguments("Sales", null));
        AegisException noRow = await Assert.ThrowsAsync<AegisException>(() => GroupWrites.PlanAsync(Directory(), GroupRelation.Members, true, [], Ct));

        Assert.Equal(FailureKind.Usage, noUser.Failure.Kind);
        Assert.Equal(FailureKind.Usage, noRow.Failure.Kind);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnAdminApiThatCannotListFallsBackToGraphAndAMemberWithoutNamesGoesByItsId(bool verbose)
    {
        var sidecar = new ScriptedRunner(_ => new ExoResult([], []));
        var adminApi = new ScriptedRunner(_ => throw new AegisException(FailureKind.ServiceError, "the Admin API is down", "later"));
        await using var backends = new ExchangeBackends(sidecar, adminApi, Directory);
        string home = PlanSamples.TempHome();

        WritePlan write;
        using (LogSink.UseVerbose(verbose ? LogSink.ForHome(home) : null))
        {
            write = await GroupWrites.PlanAsync(backends, GroupRelation.Members, true, GroupWrites.FromArguments(Sales, [Device]), Ct);
        }

        CommandResult<Plan> applied = await PlanGuard.RunAsync(write, WriteMode.Apply, PlanSamples.Store(home), Ct);

        Assert.Equal($"{Device} (directoryObject, {Device}) as a member", Assert.Single(write.Plan.Changes).Detail);
        Assert.True(applied.Ok);
        ExoCommand command = Assert.Single(sidecar.Commands);
        Assert.Equal(Device, command.Parameters["Member"]);
        Assert.Equal(Sales, command.Parameters["Identity"]);
        Assert.Equal(verbose, System.IO.Directory.Exists(Path.Combine(home, LogSink.FolderName)));
    }

    [Fact]
    public async Task EveryShapeOfExchangeMemberIsMatchedAndRemovedByTheIdentityExchangeGaveIt()
    {
        var sidecar = new ScriptedRunner(_ => new ExoResult([], []));
        var adminApi = new ScriptedRunner(_ => new ExoResult(
            [
                Recipient(new JsonObject { ["Guid"] = "a-guid", ["Name"] = "A", ["MicrosoftOnlineServicesID"] = "a@contoso.com" }),
                Recipient(new JsonObject { ["RecipientTypeDetails"] = "MailUser", ["Identity"] = "B", ["Name"] = "B" }),
                Recipient(new JsonObject { ["RecipientTypeDetails"] = "SomethingNew", ["Name"] = "C" }),
                Recipient(new JsonObject
                {
                    ["RecipientTypeDetails"] = "UserMailbox",
                    ["PrimarySmtpAddress"] = "dave@contoso.com",
                    ["ExternalDirectoryObjectId"] = "00000000-0000-0000-0000-000000000067",
                    ["DisplayName"] = "Dave",
                    ["WindowsLiveID"] = "dave@contoso.com",
                    ["EmailAddresses"] = new JsonArray("SMTP:dave@contoso.com", "smtp:dave.old@contoso.com", "X500:/o=contoso"),
                }),
                Recipient(new JsonObject { ["RecipientTypeDetails"] = "MailUniversalDistributionGroup", ["Name"] = "Nested", ["PrimarySmtpAddress"] = "nested@contoso.com" }),
                Recipient(new JsonObject { ["RecipientTypeDetails"] = "MailContact", ["Name"] = "Erin", ["PrimarySmtpAddress"] = "erin@fabrikam.com" }),
                Recipient(new JsonObject { ["Guid"] = "f-guid", ["MicrosoftOnlineServicesID"] = "frank@contoso.com" }),
            ],
            []));
        await using var backends = new ExchangeBackends(sidecar, adminApi, Directory);

        WritePlan write = await GroupWrites.PlanAsync(
            backends, GroupRelation.Members, false, GroupWrites.FromArguments(Sales, ["a@contoso.com", "B", "C", "dave.old@contoso.com", "frank@contoso.com", "nobody"]), Ct);
        CommandResult<Plan> applied = await PlanGuard.RunAsync(write, WriteMode.Apply, PlanSamples.Store(PlanSamples.TempHome()), Ct);

        Assert.Equal(
            ["A (a@contoso.com) as a member", "B (user, B) as a member", "C (recipient, ?) as a member", "Dave (dave@contoso.com) as a member", "f-guid (frank@contoso.com) as a member"],
            write.Plan.Changes.Select(change => change.Detail));
        Assert.Equal("nobody as a member", Assert.Single(write.Plan.Unchanged).Detail);
        Assert.True(applied.Ok);
        Assert.Equal(["a-guid", "B", "C", "dave@contoso.com", "f-guid"], sidecar.Commands.Select(command => command.Parameters["Member"]));
    }

    [Theory]
    [InlineData(GroupRelation.Members, "members")]
    [InlineData(GroupRelation.Owners, "owners")]
    public async Task ASyncedGroupIsWarnedAbout(GroupRelation relation, string list)
    {
        WritePlan write = await GroupWrites.PlanAsync(Directory(), relation, true, GroupWrites.FromArguments(Synced, ["alice@contoso.com"]), Ct);

        Assert.Contains(write.Plan.Warnings, warning => warning.Contains($"Graph cannot change its {list} there", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TheOwnersOfADistributionGroupAreNotGraphs()
    {
        WritePlan write = await GroupWrites.PlanAsync(Directory(), GroupRelation.Owners, true, GroupWrites.FromArguments(Sales, ["alice@contoso.com"]), Ct);

        Assert.Contains(write.Plan.Warnings, warning => warning.Contains("Graph keeps no owners for it", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RemovingOneOfTwoOwnersKeepsAnOwner()
    {
        WritePlan write = await GroupWrites.PlanAsync(Directory(), GroupRelation.Owners, false, GroupWrites.FromArguments(Project, ["bob@contoso.com"]), Ct);

        Assert.Single(write.Plan.Changes);
        Assert.Empty(write.Plan.Warnings);
    }

    [Fact]
    public async Task AWriteOverTwoGroupsIsTitledByTheirCountAndAMemberAlreadyThereNeedsNoLookup()
    {
        var transport = new ScriptedTransport(Answer);
        var graph = new GraphClient(transport);

        WritePlan write = await GroupWrites.PlanAsync(
            graph, GroupRelation.Members, true, [new GroupChangeRow(Project, "alice@contoso.com"), new GroupChangeRow(Synced, Alice)], Ct);

        Assert.Equal("Add members to 2 groups", write.Plan.Title);
        Assert.Single(write.Plan.Unchanged);
        Assert.DoesNotContain(transport.Requests, request => request.Uri.AbsolutePath.EndsWith("/$batch", StringComparison.Ordinal)
            && request.Body!.Contains("alice", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnAllKnownAddResolvesNothing()
    {
        var transport = new ScriptedTransport(Answer);

        WritePlan write = await GroupWrites.PlanAsync(new GraphClient(transport), GroupRelation.Members, true, GroupWrites.FromArguments(Project, ["alice@contoso.com"]), Ct);

        Assert.Empty(write.Plan.Changes);
        Assert.DoesNotContain(transport.Requests, request => request.Uri.AbsolutePath.EndsWith("/$batch", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnUnknownUserOfACsvLineIsNotFoundNamingTheLine()
    {
        AegisException exception = await Assert.ThrowsAsync<AegisException>(() =>
            GroupWrites.PlanAsync(Directory(), GroupRelation.Members, true, [new GroupChangeRow(Project, "ghost@contoso.com", 3)], Ct));

        Assert.Equal(FailureKind.NotFound, exception.Failure.Kind);
        Assert.Contains("(line 3)", exception.Failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AUserAnsweredWithItsTypeKeepsIt()
    {
        WritePlan write = await GroupWrites.PlanAsync(Directory(), GroupRelation.Members, true, GroupWrites.FromArguments(Project, ["carol@contoso.com"]), Ct);

        Assert.Equal("Carol (carol@contoso.com) as a member", Assert.Single(write.Plan.Changes).Detail);
    }

    private static ExoObject Recipient(JsonObject values) => new(JsonSerializer.SerializeToElement(values));

    private static GraphClient Directory() => new(new ScriptedTransport(Answer));

    /// <summary>The scripted directory: the three groups, their lists, and the users a batch looks up.</summary>
    private static GraphResponse Answer(GraphHttpRequest request)
    {
        string path = Uri.UnescapeDataString(request.Uri.AbsolutePath);
        if (path.EndsWith("/$batch", StringComparison.Ordinal))
        {
            var responses = new JsonArray();
            foreach (JsonNode? item in JsonNode.Parse(request.Body!)!["requests"]!.AsArray())
            {
                string url = item!["url"]!.GetValue<string>();
                JsonObject answer = url switch
                {
                    _ when url.StartsWith("/users/alice", StringComparison.Ordinal) => Ok(User(Alice, "Alice", "alice@contoso.com")),
                    _ when url.StartsWith("/users/carol", StringComparison.Ordinal) => Ok(new JsonObject
                    {
                        ["@odata.type"] = "#microsoft.graph.user",
                        ["id"] = "00000000-0000-0000-0000-000000000068",
                        ["displayName"] = "Carol",
                        ["userPrincipalName"] = "carol@contoso.com",
                    }),
                    _ when url.StartsWith($"/directoryObjects/{Alice}", StringComparison.Ordinal) => Ok(User(Alice, "Alice", "alice@contoso.com")),
                    _ when url.StartsWith($"/directoryObjects/{Device}", StringComparison.Ordinal) => new JsonObject { ["status"] = 200 },
                    _ => new JsonObject { ["status"] = 404 },
                };
                answer["id"] = item["id"]!.GetValue<string>();
                responses.Add(answer);
            }

            return Json(new JsonObject { ["responses"] = responses });
        }

        JsonArray list = path switch
        {
            _ when path.EndsWith($"/groups/{Project}/members", StringComparison.Ordinal) => new JsonArray(User(Alice, "Alice", "alice@contoso.com")),
            _ when path.EndsWith($"/groups/{Project}/owners", StringComparison.Ordinal) =>
                new JsonArray(User(Alice, "Alice", "alice@contoso.com"), User(Bob, "Bob", "bob@contoso.com")),
            _ => [],
        };
        if (path.EndsWith("/members", StringComparison.Ordinal) || path.EndsWith("/owners", StringComparison.Ordinal))
        {
            return Json(new JsonObject { ["value"] = list });
        }

        return Json(path[(path.LastIndexOf('/') + 1)..] switch
        {
            Sales => new JsonObject { ["id"] = Sales, ["displayName"] = "Sales", ["securityEnabled"] = false },
            Synced => new JsonObject { ["id"] = Synced, ["displayName"] = "Synced", ["groupTypes"] = new JsonArray("Unified"), ["onPremisesSyncEnabled"] = true },
            _ => new JsonObject { ["id"] = Project, ["displayName"] = "Project", ["groupTypes"] = new JsonArray("Unified"), ["mail"] = "project@contoso.com" },
        });
    }

    private static JsonObject User(string id, string name, string upn) =>
        new() { ["@odata.type"] = "#microsoft.graph.user", ["id"] = id, ["displayName"] = name, ["userPrincipalName"] = upn };

    private static JsonObject Ok(JsonObject body) => new() { ["status"] = 200, ["body"] = body };

    private static GraphResponse Json(JsonObject body) => new(200, [], body.ToJsonString());

    /// <summary>A cmdlet runner that answers from a script and records every command.</summary>
    private sealed class ScriptedRunner(Func<ExoCommand, ExoResult> answer) : IExoCmdletRunner
    {
        internal List<ExoCommand> Commands { get; } = [];

        public Task<ExoResult> InvokeAsync(ExoCommand command, CancellationToken cancellationToken)
        {
            Commands.Add(command);
            return Task.FromResult(answer(command));
        }
    }
}
