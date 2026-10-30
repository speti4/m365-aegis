using M365Aegis.Core.Operations;
using M365Aegis.Core.Output;
using M365Aegis.Tests;

namespace M365Aegis.Core.Tests.Operations;

/// <summary>
/// The prune and the resume over the edges the round's own tests do not reach (task 070): a record without its start
/// time, a file the file system will not remove, and a pending change without object ids or a line.
/// </summary>
public sealed class PlanPruneEdgeTests
{
    [Fact]
    public void ARecordWithoutItsStartIsAgedByItsFile()
    {
        string home = PlanSamples.TempHome();
        var plan = new Plan("group-members-add", "Add members to Finance", [], [], []);
        string path = PlanSamples.Store(home).Begin(plan);
        File.SetLastWriteTimeUtc(path, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        WritePlan prune = PlanPrune.Plan(home, 30, PlanSamples.Now);

        Assert.Equal(Path.GetFileName(path), Assert.Single(prune.Plan.Changes).Target);
    }

    [Fact]
    public void AFileTheFileSystemWillNotRemoveIsForbidden()
    {
        // A folder where the record's file was: File.Delete refuses it on every system.
        string folder = Directory.CreateTempSubdirectory("m365-aegis-tests-").FullName;

        AegisException exception = Assert.Throws<AegisException>(() => PlanPrune.Remove(folder, "record.json"));

        Assert.Equal(FailureKind.Forbidden, exception.Failure.Kind);
        Assert.Contains("record.json", exception.Failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void APendingChangeWithoutIdsOrALineIsMatchedByItsText()
    {
        var pending = new PlanChange(PlanAction.Add, "Finance", "Bob as a member", "not a member", "a member");
        var fresh = new Plan("group-members-add", "Add members to Finance", [pending with { Current = "-" }], [], []);

        Assert.True(PlanResume.Continued(pending, fresh, [0], []));
        Assert.False(PlanResume.Continued(pending with { Detail = "Carol as a member" }, fresh, [0], []));
    }

    [Fact]
    public void AChangeOfTheSameObjectsButAnotherActionDoesNotContinueIt()
    {
        var pending = new PlanChange(PlanAction.Add, "Finance", "Bob as a member", "not a member", "a member") { Key = "group:1|user:2" };
        var fresh = new Plan("group-members-add", "Add members to Finance", [pending with { Action = PlanAction.Remove }], [], []);

        Assert.False(PlanResume.Continued(pending, fresh, [0], []));
        Assert.True(PlanResume.Continued(pending, fresh with { Changes = [pending] }, [0], []));
    }
}
