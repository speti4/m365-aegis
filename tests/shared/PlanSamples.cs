using M365Aegis.Core.Operations;
using M365Aegis.Core.Output;

namespace M365Aegis.Tests;

/// <summary>
/// The plans of the write tests (task 051): a clock that stands still, a plan store in a temporary home, a made-up
/// plan whose steps succeed or fail as the test says, the sample CSV files under tests/fixtures/csv, and the scrub
/// that keeps a temporary folder out of a snapshot. Linked into both test projects.
/// </summary>
internal static class PlanSamples
{
    /// <summary>The moment every sample apply starts and ends.</summary>
    internal static readonly DateTimeOffset Now = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);

    /// <summary>The folder of the sample CSV files: the repo's tests/fixtures/csv.</summary>
    internal static string CsvFolder { get; } = Path.Combine(Path.GetDirectoryName(GraphFixtures.Root)!, "csv");

    /// <summary>A sample CSV file's path.</summary>
    internal static string Csv(string name) => Path.Combine(CsvFolder, name);

    /// <summary>A new, empty temporary home folder (not created).</summary>
    internal static string TempHome() => Path.Combine(Path.GetTempPath(), "m365-aegis-tests", Guid.NewGuid().ToString("N"));

    /// <summary>A store under <paramref name="home"/> whose clock stands at <see cref="Now"/>.</summary>
    internal static PlanStore Store(string home) =>
        new(home, new PlanRecordContext("tests", null, ["group", "members", "add"]), new ManualClock(Now));

    /// <summary>
    /// A made-up member write of two changes and one object already as asked; <paramref name="second"/> is what the
    /// second change's step throws (null: it succeeds). <paramref name="sent"/> collects the steps that ran.
    /// </summary>
    internal static WritePlan GroupWrite(List<string> sent, AegisException? second = null, bool fromFile = false) =>
        new PlanBuilder("group-members-add", "Add members to Finance (00000000-0000-0000-0000-000000000031)")
            .Change(
                new PlanChange(PlanAction.Add, "Finance", "Dave Example (dave@contoso.com) as a member", "not a member", "a member") { Row = fromFile ? 2 : null },
                _ =>
                {
                    sent.Add("dave");
                    return Task.CompletedTask;
                })
            .Change(
                new PlanChange(PlanAction.Add, "Finance", "Eve Example (eve@contoso.com) as a member", "not a member", "a member") { Row = fromFile ? 4 : null },
                _ =>
                {
                    sent.Add("eve");
                    return second is null ? Task.CompletedTask : Task.FromException(second);
                })
            .Unchanged(new PlanUnchanged("Finance", "Bob Example (bob@contoso.com) as a member", "a member already") { Row = fromFile ? 3 : null })
            .Warn("a made-up warning: Finance is a sample")
            .Build();

    /// <summary>The 403 a step of <see cref="GroupWrite"/> may fail with.</summary>
    internal static AegisException Forbidden => new(
        FailureKind.Forbidden,
        "Microsoft Graph answered 403 to POST /groups/00000000-0000-0000-0000-000000000031/members/$ref: Authorization_RequestDenied: Insufficient privileges to complete the operation.",
        "consent GroupMember.ReadWrite.All for the app registration");

    /// <summary>A text with the temporary home written as <c>&lt;home&gt;</c> and its separators as <c>/</c>, plain or JSON-escaped.</summary>
    internal static string Scrub(string text, string home)
    {
        string escaped = home.Replace("\\", "\\\\", StringComparison.Ordinal);
        return text
            .Replace(escaped, "<home>", StringComparison.Ordinal)
            .Replace(home, "<home>", StringComparison.Ordinal)
            .Replace("<home>\\\\plans\\\\", "<home>/plans/", StringComparison.Ordinal)
            .Replace("<home>\\plans\\", "<home>/plans/", StringComparison.Ordinal);
    }

    /// <summary>A clock that stands still.</summary>
    private sealed class ManualClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
