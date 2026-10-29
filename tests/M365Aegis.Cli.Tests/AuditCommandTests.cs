using System.Text.Json;
using M365Aegis.Cli.Commands;
using M365Aegis.Cli.Output;
using M365Aegis.Core.Audit;
using M365Aegis.Core.Graph;
using M365Aegis.Core.Output;
using M365Aegis.Tests;

namespace M365Aegis.Cli.Tests;

/// <summary>
/// <c>signin list</c>, <c>secrets expiring</c> and <c>audit list</c> as a child process over the recorded answers
/// (an explicit <c>--since</c>, so the request matches a fixture whatever the date), and their tables in process at
/// a fixed moment, byte for byte.
/// </summary>
public sealed class AuditCommandTests
{
    private const string Since = "2026-10-28T12:00:00Z";

    private static readonly DateTimeOffset Now = new(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void SignInsOfAUserAreAnEnvelopeOverEveryPage()
    {
        (int exitCode, string stdout, string stderr) = CliTests.RunWith(CallCommandTests.Fixtures, "--json", "signin", "list", "--user", "alice@contoso.com", "--since", Since);

        Assert.Equal(0, exitCode);
        Assert.Equal("", stderr);
        using JsonDocument document = JsonDocument.Parse(stdout);
        JsonElement root = document.RootElement;
        Assert.True(root.GetProperty("ok").GetBoolean());
        Assert.Equal(4, root.GetProperty("data").GetProperty("signIns").GetArrayLength());
        Assert.Equal("graph", root.GetProperty("meta").GetProperty("backend").GetString());
        Assert.Equal(2, root.GetProperty("meta").GetProperty("requestIds").GetArrayLength());
    }

    [Fact]
    public void FailuresKeepOnlyTheFailedSignIns()
    {
        (int exitCode, string stdout, _) = CliTests.RunWith(CallCommandTests.Fixtures, "--json", "signin", "list", "--user", "alice@contoso.com", "--since", Since, "--failures");

        Assert.Equal(0, exitCode);
        using JsonDocument document = JsonDocument.Parse(stdout);
        JsonElement signIns = document.RootElement.GetProperty("data").GetProperty("signIns");
        Assert.All(signIns.EnumerateArray(), signIn => Assert.False(signIn.GetProperty("succeeded").GetBoolean()));
        Assert.Equal(2, signIns.GetArrayLength());
    }

    [Fact]
    public void ATenantWithoutP1ExitsEightWithAClearSentence()
    {
        (int exitCode, string stdout, string stderr) = CliTests.RunWith(CallCommandTests.Fixtures, "--table", "signin", "list", "--user", "bob@contoso.com", "--since", Since);

        Assert.Equal(8, exitCode);
        Assert.Equal("", stdout);
        Assert.StartsWith("error: the sign-in log through Microsoft Graph needs a Microsoft Entra ID P1 or P2 license", stderr, StringComparison.Ordinal);
        Assert.Contains("(forbidden)", stderr, StringComparison.Ordinal);
        Assert.DoesNotContain("   at ", stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void DaysAndSinceTogetherAreAUsageError()
    {
        (int exitCode, string stdout, _) = CliTests.RunWith(CallCommandTests.Fixtures, "--json", "signin", "list", "--days", "1", "--since", Since);

        Assert.Equal(2, exitCode);
        using JsonDocument document = JsonDocument.Parse(stdout);
        Assert.Equal("usage", document.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public void ExpiringSecretsNameTheirApplication()
    {
        // Ten years and the expired ones: every credential of the fixtures, whatever the date the test runs on.
        (int exitCode, string stdout, _) = CliTests.RunWith(CallCommandTests.Fixtures, "--json", "secrets", "expiring", "3650", "--include-expired");

        Assert.Equal(0, exitCode);
        using JsonDocument document = JsonDocument.Parse(stdout);
        JsonElement data = document.RootElement.GetProperty("data");
        Assert.Equal(4, data.GetProperty("applications").GetInt32());
        Assert.Equal(2, data.GetProperty("servicePrincipals").GetInt32());
        string?[] names = data.GetProperty("credentials").EnumerateArray().Select(credential => credential.GetProperty("displayName").GetString()).ToArray();
        Assert.Equal(["Old Integration", "Contoso SAML App", "Payroll Connector", "Reporting Bot", "Payroll Connector"], names);
        Assert.Equal("service-principal", data.GetProperty("credentials")[1].GetProperty("owner").GetString());
    }

    [Fact]
    public void ADayCountThatIsNotANumberIsAUsageError()
    {
        (int exitCode, string stdout, _) = CliTests.RunWith(CallCommandTests.Fixtures, "--json", "secrets", "expiring", "soon");

        Assert.Equal(2, exitCode);
        using JsonDocument document = JsonDocument.Parse(stdout);
        Assert.Equal("usage", document.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public void TheDirectoryAuditFiltersByActivity()
    {
        (int exitCode, string stdout, _) = CliTests.RunWith(CallCommandTests.Fixtures, "--json", "audit", "list", "--since", Since, "--activity", "Add member");

        Assert.Equal(0, exitCode);
        using JsonDocument document = JsonDocument.Parse(stdout);
        JsonElement entry = Assert.Single(document.RootElement.GetProperty("data").GetProperty("entries").EnumerateArray());
        Assert.Equal("Add member to group", entry.GetProperty("activityDisplayName").GetString());
    }

    [Fact]
    public async Task TheSignInTable()
    {
        (GraphClient graph, _) = GraphFixtures.Client();
        CommandResult<SignInReport> result = await SignInLookup.RunAsync(graph, new SignInQuery("alice@contoso.com", false, 1, null, 1000), Now, TestContext.Current.CancellationToken);

        await Verify(Render(result, graph, SignInListTableView.Instance));
    }

    [Fact]
    public async Task TheFailedSignInTableUnderTheLimit()
    {
        (GraphClient graph, _) = GraphFixtures.Client();
        CommandResult<SignInReport> result = await SignInLookup.RunAsync(graph, new SignInQuery("alice@contoso.com", true, 1, null, 1), Now, TestContext.Current.CancellationToken);

        await Verify(Render(result, graph, SignInListTableView.Instance));
    }

    [Fact]
    public async Task TheExpiringSecretsTable()
    {
        (GraphClient graph, _) = GraphFixtures.Client();
        CommandResult<CredentialExpiryReport> result = await CredentialExpiryLookup.RunAsync(graph, new CredentialExpiryQuery(30, true), Now, TestContext.Current.CancellationToken);

        await Verify(Render(result, graph, SecretsExpiringTableView.Instance));
    }

    [Fact]
    public async Task TheDirectoryAuditTable()
    {
        (GraphClient graph, _) = GraphFixtures.Client();
        CommandResult<DirectoryAuditReport> result = await DirectoryAuditLookup.RunAsync(graph, new DirectoryAuditQuery(1, null, null, 1000), Now, TestContext.Current.CancellationToken);

        await Verify(Render(result, graph, AuditListTableView.Instance));
    }

    private static string Render<T>(CommandResult<T> result, GraphClient graph, ITableView<T> view)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        Renderer.Render(result, new EnvelopeMeta(graph.RequestLog.Ids, 12, Backend.Graph), view, OutputMode.Table, output, error);
        return $"--- stdout ---\n{output}--- stderr ---\n{error}";
    }
}
