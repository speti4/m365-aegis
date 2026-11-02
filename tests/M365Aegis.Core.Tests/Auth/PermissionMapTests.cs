using System.Text.RegularExpressions;
using M365Aegis.Core.Auth;

namespace M365Aegis.Core.Tests.Auth;

/// <summary>
/// The command-to-permission map of the doctor's <c>consent</c> row held to the setup guide (task 109): the
/// permissions table of docs/setup.md read back row by row (its section, its first column, the permissions its second
/// column names) and compared with <see cref="PermissionMap.Rows"/> both ways, and the guide's Azure CLI lists per API
/// compared with the map's permissions per resource. A row added to the guide, a permission renamed in one place or
/// an API mixed up fails here.
/// </summary>
public sealed partial class PermissionMapTests
{
    private static readonly string[] Guide = SetupGuide.Lines;

    [Fact]
    public void TheMapHoldsEveryRowOfTheTableThatNamesAPermissionInTheGuidesOrder()
    {
        List<string> table = TableRows();

        Assert.NotEmpty(table);
        Assert.Equal(table, PermissionMap.Rows.Select(row => Row(row.Section, row.Commands, row.Permissions.Select(permission => permission.Name))));
    }

    [Fact]
    public void TheAzureCliListsOfTheGuideAreTheMapsPermissionsPerApi()
    {
        Assert.Equal(CliList("graph").Order(StringComparer.Ordinal), MapNames(Resource.Graph));
        Assert.Equal(CliList("exchange").Order(StringComparer.Ordinal), MapNames(Resource.Exchange));
    }

    [Fact]
    public void TheMapNamesEachPermissionOnceForOneApi()
    {
        Assert.Equal(PermissionMap.Permissions.Count, PermissionMap.Permissions.Select(permission => permission.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(PermissionMap.Rows, row => Assert.Equal(row.Permissions.Count, row.Permissions.Distinct().Count()));
    }

    [Fact]
    public void TheApisHaveTheGuidesNames()
    {
        Assert.Equal("Microsoft Graph", PermissionMap.ApiName(Resource.Graph));
        Assert.Equal("Exchange Online", PermissionMap.ApiName(Resource.Exchange));
        Assert.Equal("?", PermissionMap.ApiName((Resource)99));
    }

    /// <summary>One row as the comparison reads it: <c>section | commands | permission, permission</c>.</summary>
    private static string Row(string section, string commands, IEnumerable<string> permissions) =>
        $"{section} | {commands} | {string.Join(", ", permissions)}";

    private static IEnumerable<string> MapNames(Resource resource) =>
        PermissionMap.Permissions.Where(permission => permission.Resource == resource).Select(permission => permission.Name).Order(StringComparer.Ordinal);

    /// <summary>
    /// The rows of the tables under "## Permissions" that name a permission: the section is the <c>###</c> heading above
    /// the table, the commands the first cell without its backticks, the permissions the backticked names of the second
    /// cell (a flag such as <c>--all-types</c> is not one), each once. The resource is left to the map; the Azure CLI
    /// lists hold it.
    /// </summary>
    private static List<string> TableRows() =>
    [
        .. SetupGuide.PermissionsTable()
            .Select(row => (row, Permissions: PermissionName().Matches(row.Cells[1]).Select(match => match.Groups["name"].Value).Distinct(StringComparer.Ordinal).ToList()))
            .Where(row => row.Permissions.Count > 0)
            .Select(row => Row(row.row.Section, row.row.Commands, row.Permissions)),
    ];

    /// <summary>The names of one <c>Add-DelegatedPermission $graph …</c> or <c>$exchange …</c> call of the Azure CLI block.</summary>
    private static List<string> CliList(string api)
    {
        int call = Array.FindIndex(Guide, line => line.StartsWith($"Add-DelegatedPermission ${api} ", StringComparison.Ordinal));
        Assert.True(call >= 0, $"docs/setup.md has no 'Add-DelegatedPermission ${api}' line");
        // The call goes on over the indented lines below it.
        IEnumerable<string> lines = Guide.Skip(call + 1).TakeWhile(line => line.StartsWith("    ", StringComparison.Ordinal)).Prepend(Guide[call][$"Add-DelegatedPermission ${api} ".Length..]);
        return [.. string.Join(" ", lines).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)];
    }

    /// <summary>A delegated permission's name in backticks: dotted words, the first capitalized (<c>User.ReadBasic.All</c>).</summary>
    [GeneratedRegex(@"`(?<name>[A-Z][A-Za-z]+(?:\.[A-Za-z0-9]+)+)`")]
    private static partial Regex PermissionName();
}
