using System.Text.Json;
using System.Text.Json.Nodes;
using M365Aegis.Core.Operations;
using M365Aegis.Core.Output;

namespace M365Aegis.Tests;

/// <summary>
/// Three made-up plan records in a temporary home (tasks 060 and 062), written through <see cref="PlanStore"/> as an
/// apply writes them, over the names of the recorded Graph answers: an owner added to Finance in full (2026-10-28), a
/// CSV batch of three mailbox grants with one failed line (2026-10-29), and a member write to Finance that a sign-in
/// failure stopped (2026-10-30): Alice applied, Bob and Dave failed, Carol, Eve and the group Finance EU skipped. Against
/// the recorded answers today Alice, Bob, Carol and Finance EU are members and Dave and Eve are not, so a resume of the
/// third plans Dave and Eve and finds Bob, Carol and Finance EU as asked. Linked into both test projects.
/// </summary>
internal static class PlanRecordSamples
{
    /// <summary>The record of the owner added in full.</summary>
    internal const string Owners = "20261028T090000.000Z-group-owners-add.json";

    /// <summary>The record of the CSV batch with one failed line.</summary>
    internal const string Grants = "20261029T100000.000Z-mailbox-access-grant.json";

    /// <summary>The record of the member write that was stopped.</summary>
    internal const string Stopped = "20261030T110000.000Z-group-members-add.json";

    /// <summary>The arguments of the stopped member write.</summary>
    internal static readonly string[] StoppedArguments =
    [
        "--json", "group", "members", "add", "Finance", "alice@contoso.com", "bob@contoso.com", "dave@contoso.com", "carol@contoso.com",
        "eve@contoso.com", "00000000-0000-0000-0000-000000000032", "--yes",
    ];

    private static readonly EnvelopeError Throttled = new("throttled", "Microsoft Graph answered 429 to POST /groups/00000000-0000-0000-0000-000000000031/members/$ref", "the retries ran out; run the command again in a minute");
    private static readonly EnvelopeError SignIn = new("auth", "Microsoft Graph answered 401 to POST /$batch", "the token was refused; run `m365-aegis login` and try again");

    /// <summary>Writes the three records under <paramref name="home"/>'s <c>plans</c> folder.</summary>
    internal static void Write(string home)
    {
        Record(home, new DateTimeOffset(2026, 10, 28, 9, 0, 0, TimeSpan.Zero), ["group", "owners", "add", "Finance", "bob@contoso.com", "--yes"], null, new Plan(
            "group-owners-add",
            "Add owners to Finance (00000000-0000-0000-0000-000000000031)",
            [Applied(Add("Bob Example (bob@contoso.com) as an owner", "not an owner", "an owner"))],
            [],
            []));

        Record(home, new DateTimeOffset(2026, 10, 29, 10, 0, 0, TimeSpan.Zero), ["mailbox", "access", "grant", "--from-csv", "mailbox-grants.csv", "--yes"], new PlanRecordInput("mailbox-grants.csv", "mailbox,user,right\nfinance,bob@contoso.com,FullAccess\nfinance,dave@example.com,SendAs\nreception,carol@contoso.com,SendOnBehalf\n"), new Plan(
            "mailbox-access-grant",
            "Grant mailbox rights on 2 mailboxes",
            [
                Applied(new PlanChange(PlanAction.Add, "Finance (finance@contoso.com)", "FullAccess for Bob Example (bob@contoso.com)", "no FullAccess", "holds FullAccess") { Row = 2 }),
                new PlanChange(PlanAction.Add, "Finance (finance@contoso.com)", "Send As for Dave Private (dave@example.com)", "no Send As", "holds Send As")
                {
                    Row = 3,
                    Result = new ChangeResult(ChangeStatus.Failed, new EnvelopeError("cmdlet-failed", "The user 'Dave Private' isn't a valid security principal for a mailbox permission.", null)),
                },
                Applied(new PlanChange(PlanAction.Add, "Reception (reception@contoso.com)", "Send on Behalf for Carol Example (carol@contoso.com)", "no Send on Behalf", "holds Send on Behalf") { Row = 4 }),
            ],
            [],
            []));

        Record(home, new DateTimeOffset(2026, 10, 30, 11, 0, 0, TimeSpan.Zero), StoppedArguments, null, new Plan(
            "group-members-add",
            "Add members to Finance (00000000-0000-0000-0000-000000000031)",
            [
                Applied(Add("Alice Example (alice@contoso.com) as a member")),
                Add("Bob Example (bob@contoso.com) as a member") with { Result = new ChangeResult(ChangeStatus.Failed, Throttled) },
                Add("Dave Example (dave@contoso.com) as a member") with { Result = new ChangeResult(ChangeStatus.Failed, SignIn) },
                Add("Carol Example (carol@contoso.com) as a member") with { Result = new ChangeResult(ChangeStatus.Skipped) },
                Add("Eve Example (eve@contoso.com) as a member") with { Result = new ChangeResult(ChangeStatus.Skipped) },
                Add("Finance EU (finance-eu@contoso.com) as a member") with { Result = new ChangeResult(ChangeStatus.Skipped) },
            ],
            [],
            []));
    }

    private static PlanChange Add(string detail, string current = "not a member", string desired = "a member") =>
        new(PlanAction.Add, "Finance", detail, current, desired);

    private static PlanChange Applied(PlanChange change) => change with { Result = new ChangeResult(ChangeStatus.Applied) };

    /// <summary>Writes a record as an apply does, started at <paramref name="started"/>, and returns its file name.</summary>
    internal static string Record(string home, DateTimeOffset started, IReadOnlyList<string> arguments, PlanRecordInput? input, Plan plan)
    {
        var store = new PlanStore(home, new PlanRecordContext("command line", null, arguments) { Input = input });
        Plan applying = plan with { Mode = PlanMode.Applying, Started = started, Changes = plan.Changes.Select(change => change with { Result = null }).ToList() };
        string path = store.Begin(applying);
        store.Finish(path, plan with { Mode = PlanMode.Applied, Started = started, Finished = started.AddSeconds(2), Record = path });
        return Path.GetFileName(path);
    }

    /// <summary>
    /// Rewrites a record as a build before task 065 wrote it: version 1, no row's place and no object ids on its changes
    /// and on what was already as asked.
    /// </summary>
    internal static void AsVersionOne(string path)
    {
        JsonObject record = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        record["version"] = 1;
        JsonObject plan = record["plan"]!.AsObject();
        foreach (JsonNode? entry in plan["changes"]!.AsArray().Concat(plan["unchanged"]!.AsArray()))
        {
            entry!.AsObject().Remove("index");
            entry.AsObject().Remove("key");
        }

        File.WriteAllText(path, record.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>
    /// The record of a CSV batch of version 2 (task 065): Finance's members from a file of two lines, the first (nobody,
    /// a user deleted since) applied, the second (Dave) failed, each change with its row's place and its object ids. A
    /// resume plans only Dave's line, so the deleted user is never read again.
    /// </summary>
    internal static string WriteBatch(string home) => Record(
        home,
        new DateTimeOffset(2026, 10, 30, 10, 0, 0, TimeSpan.Zero),
        ["group", "members", "add", "--from-csv", "members.csv", "--yes"],
        new PlanRecordInput("members.csv", "group,user\nFinance,nobody@contoso.com\nFinance,dave@contoso.com\n"),
        new Plan(
            "group-members-add",
            "Add members to Finance (00000000-0000-0000-0000-000000000031)",
            [
                Applied(Add("Nobody Example (nobody@contoso.com) as a member") with { Row = 2, Index = 1, Key = "00000000-0000-0000-0000-000000000031/00000000-0000-0000-0000-000000000009" }),
                Add("Dave Example (dave@contoso.com) as a member") with
                {
                    Row = 3,
                    Index = 2,
                    Key = "00000000-0000-0000-0000-000000000031/00000000-0000-0000-0000-000000000005",
                    Result = new ChangeResult(ChangeStatus.Failed, Throttled),
                },
            ],
            [],
            []));
}
