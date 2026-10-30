using System.Globalization;
using M365Aegis.Core.Output;

namespace M365Aegis.Core.Operations;

/// <summary>
/// <c>plan prune --older-than &lt;days&gt;</c> (task 066): the plan records an apply started more than that many days ago
/// go from the home folder's <c>plans</c> folder. It is a write like every other, only local: a <see cref="WritePlan"/>
/// of one change per record to remove, which <c>--dry-run</c> prints, <c>--yes</c> applies through
/// <see cref="PlanGuard"/> (whose own record of the prune stays, being new) and neither refuses with exit code 7. No
/// tenant is read or written. A file in the folder that is not a plan record is never removed, and the newer records
/// are left as they are. A record that left changes to do warns that its resume goes with it. The folder has no size
/// limit of its own: nothing removes a record without a plan the administrator confirmed.
/// </summary>
public static class PlanPrune
{
    /// <summary>The operation's stable name.</summary>
    public const string Operation = "plan-prune";

    /// <summary>
    /// The plan of the prune: a change per record started before <paramref name="now"/> less
    /// <paramref name="olderThanDays"/> days, oldest first.
    /// </summary>
    /// <param name="home">The home folder.</param>
    /// <param name="olderThanDays">The age in days a record must exceed to go, at least 1, so an apply running now is never touched.</param>
    /// <param name="now">The moment the age counts from.</param>
    /// <exception cref="AegisException">A usage error for an age below one day.</exception>
    public static WritePlan Plan(string home, int olderThanDays, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(home);
        if (olderThanDays < 1)
        {
            throw new AegisException(
                FailureKind.Usage,
                string.Format(CultureInfo.InvariantCulture, "--older-than {0}: the age is a whole number of days, at least 1", olderThanDays),
                "a record of an apply running now is never removed; give --older-than 30 to keep the last thirty days");
        }

        DateTimeOffset cutoff = now.AddDays(-olderThanDays);
        string folder = Path.Combine(home, "plans");
        var builder = new PlanBuilder(Operation, "");
        int total = 0;
        if (Directory.Exists(folder))
        {
            foreach (string file in Directory.EnumerateFiles(folder, "*.json").Order(StringComparer.Ordinal))
            {
                if (PlanStore.TryRead(file) is not { } record)
                {
                    continue;
                }

                total++;
                DateTimeOffset started = record.Plan.Started ?? new DateTimeOffset(File.GetLastWriteTimeUtc(file), TimeSpan.Zero);
                if (started >= cutoff)
                {
                    continue;
                }

                string name = Path.GetFileName(file);
                int left = record.Plan.Changes.Count(change => change.Result?.Status is null or ChangeStatus.Failed or ChangeStatus.Skipped);
                string detail = string.Format(
                    CultureInfo.InvariantCulture,
                    "the record of {0}, started {1:yyyy-MM-dd HH:mm:ss} UTC, {2} change(s), {3} byte(s)",
                    record.Plan.Operation,
                    started.UtcDateTime,
                    record.Plan.Changes.Count,
                    new FileInfo(file).Length);
                string path = file;
                builder.Change(new PlanChange(PlanAction.Remove, name, detail, "kept", "removed") { Index = total, Key = name }, _ =>
                {
                    Remove(path, name);
                    return Task.CompletedTask;
                });
                if (left > 0)
                {
                    builder.Warn(string.Format(
                        CultureInfo.InvariantCulture,
                        "{0} left {1} change(s) to do: once it is removed, --resume cannot continue them (`m365-aegis plan show {0}` lists them)",
                        name,
                        left));
                }
            }
        }

        builder.Title = string.Format(
            CultureInfo.InvariantCulture,
            "Remove the plan records started before {0:yyyy-MM-dd HH:mm:ss} UTC (older than {1} day(s)): {2} of {3}",
            cutoff.UtcDateTime,
            olderThanDays,
            builder.Changes,
            total);
        return builder.Build();
    }

    /// <summary>Removes one record; a file gone in the meantime is removed already.</summary>
    /// <exception cref="AegisException">The file system refused (<see cref="FailureKind.Forbidden"/>, exit code 8).</exception>
    private static void Remove(string path, string name)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new AegisException(
                FailureKind.Forbidden,
                $"the file system refused to remove {name}: {exception.Message}",
                "check the plans folder's permissions, or close the program that holds the file, and run the prune again",
                exception);
        }
    }
}
