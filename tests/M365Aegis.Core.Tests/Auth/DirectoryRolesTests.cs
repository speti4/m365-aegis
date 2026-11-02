using M365Aegis.Core.Auth;
using M365Aegis.Core.Diagnostics;
using M365Aegis.Core.Graph;
using M365Aegis.Core.Output;
using M365Aegis.Core.Tests.Config;
using M365Aegis.Core.Tests.Graph;

namespace M365Aegis.Core.Tests.Auth;

/// <summary>
/// The account's Entra roles for the doctor's <c>roles</c> row (task 111): the directory roles of
/// <c>/me/transitiveMemberOf</c> as an advanced query, each template id once, and a role without its template id a
/// failed read rather than a missing role.
/// </summary>
public sealed class DirectoryRolesTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheRolesAreTheTemplateIdsOfTheDirectoryRolesOfTheSignedInAccountEachOnce()
    {
        var transport = new ScriptedTransport(_ => new GraphResponse(200, [], """
            {"value":[
              {"@odata.type":"#microsoft.graph.directoryRole","id":"00000000-0000-0000-0000-000000000031","displayName":"Global Reader","roleTemplateId":"f2ef992c-3afb-46b9-b7cf-a126ee74c451"},
              {"@odata.type":"#microsoft.graph.directoryRole","id":"00000000-0000-0000-0000-000000000032","displayName":"Reports Reader","roleTemplateId":"4a5d8f65-41da-4de4-8968-e035b65339cf"},
              {"@odata.type":"#microsoft.graph.directoryRole","id":"00000000-0000-0000-0000-000000000031","displayName":"Global Reader","roleTemplateId":"f2ef992c-3afb-46b9-b7cf-a126ee74c451"}
            ]}
            """));

        IReadOnlyList<Guid> roles = await DirectoryRoles.ReadAsync(new GraphClient(transport), Ct);

        Assert.Equal([RoleMap.GlobalReader.TemplateId, RoleMap.ReportsReader.TemplateId], roles);
        GraphHttpRequest request = Assert.Single(transport.Requests);
        Assert.StartsWith("https://graph.microsoft.com/v1.0/me/transitiveMemberOf/microsoft.graph.directoryRole?$count=true&$select=id", request.Uri.OriginalString, StringComparison.Ordinal);
        Assert.Equal("eventual", request.Headers["ConsistencyLevel"]);
    }

    [Fact]
    public async Task ARoleWithoutItsTemplateIdFailsTheRead()
    {
        var transport = new ScriptedTransport(_ => new GraphResponse(200, [], """
            {"value":[{"@odata.type":"#microsoft.graph.directoryRole","id":"00000000-0000-0000-0000-000000000031"}]}
            """));

        AegisException exception = await Assert.ThrowsAsync<AegisException>(() => DirectoryRoles.ReadAsync(new GraphClient(transport), Ct));

        Assert.Equal(FailureKind.ServiceError, exception.Failure.Kind);
        Assert.Contains("without its template id (00000000-0000-0000-0000-000000000031)", exception.Failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheRealMachinesReadAsksTheProfileFirst()
    {
        DoctorEnvironment environment = DoctorEnvironment.ForThisMachine(ProfileStoreTests.TempHome(), "no-such-profile");

        await Assert.ThrowsAsync<AegisException>(() => environment.ReadDirectoryRoles("no-such-profile", Ct));
    }
}
