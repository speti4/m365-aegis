using M365Aegis.Core.Auth;

namespace M365Aegis.Core.Tests.Auth;

/// <summary>
/// The role column of the doctor's <c>roles</c> row held to the setup guide (task 111): every row of docs/setup.md's
/// permissions tables whose role column names a role is a row of <see cref="RoleMap.Rows"/> in the guide's order, every
/// Entra role the column names is one the row accepts, and a row is judged as an Exchange Online role group exactly
/// when its column names one. A role added to the guide, a row renamed or an Exchange row treated as an Entra one
/// fails here.
/// </summary>
public sealed class RoleMapTests
{
    [Fact]
    public void TheMapHoldsEveryRowOfTheTableThatNamesARoleInTheGuidesOrder()
    {
        List<string> table = [.. RoleRows().Select(row => $"{row.Section} | {row.Commands}")];

        Assert.NotEmpty(table);
        Assert.Equal(table, RoleMap.Rows.Select(row => $"{row.Section} | {row.Commands}"));
    }

    [Fact]
    public void EveryEntraRoleTheGuideNamesForARowIsOneTheRowAccepts()
    {
        foreach ((GuideRow guide, RoleRow row) in RoleRows().Zip(RoleMap.Rows))
        {
            IEnumerable<string> named = RoleMap.Roles.Select(role => role.Name).Where(role => guide.Cells[3].Contains(role, StringComparison.Ordinal));
            Assert.All(named, role => Assert.Contains(role, row.AnyOf.Select(accepted => accepted.Name)));
        }
    }

    [Fact]
    public void ARowIsAnExchangeRowExactlyWhenTheGuideNamesAnExchangeOnlineRole()
    {
        foreach ((GuideRow guide, RoleRow row) in RoleRows().Zip(RoleMap.Rows))
        {
            bool exchange = guide.Cells[3].Contains("Management", StringComparison.Ordinal) || guide.Cells[3].Contains("Exchange Online", StringComparison.Ordinal);
            Assert.True(exchange == (row.Kind == RoleKind.Exchange), $"the role kind of '{row.Commands}'");
            Assert.True(guide.Cells[3].Contains("group's owner", StringComparison.Ordinal) == (row.Kind == RoleKind.EntraOrOwner), $"the owner clause of '{row.Commands}'");
        }
    }

    [Fact]
    public void GlobalAdministratorAnswersEveryRowAndEachRowNamesARoleOnce()
    {
        Assert.All(RoleMap.Rows, row => Assert.Contains(RoleMap.GlobalAdministrator, row.AnyOf));
        Assert.All(RoleMap.Rows, row => Assert.Equal(row.AnyOf.Count, row.AnyOf.Distinct().Count()));
        Assert.All(RoleMap.Rows, row => Assert.True(row.AnyOf.Count >= 2, $"'{row.Commands}' names a single role"));
        Assert.Equal(RoleMap.Roles.Count, RoleMap.Roles.Select(role => role.TemplateId).Distinct().Count());
        Assert.All(RoleMap.Rows.SelectMany(row => row.AnyOf), role => Assert.Contains(role, RoleMap.Roles));
    }

    /// <summary>
    /// The rows of the guide whose role column names a role: not empty, not "what that page names", and not a plain
    /// "none" (a "none for Graph" that goes on to an Exchange Online role counts).
    /// </summary>
    private static List<GuideRow> RoleRows() =>
    [
        .. SetupGuide.PermissionsTable().Where(row =>
            row.Cells[3].Length > 0
            && !row.Cells[3].StartsWith("what ", StringComparison.Ordinal)
            && (!row.Cells[3].StartsWith("none", StringComparison.Ordinal) || row.Cells[3].Contains("Exchange Online", StringComparison.Ordinal))),
    ];
}
