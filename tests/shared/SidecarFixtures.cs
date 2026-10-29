using M365Aegis.Core.Diagnostics;
using M365Aegis.Core.Exchange;
using M365Aegis.Core.Exchange.Sidecar;

namespace M365Aegis.Tests;

/// <summary>
/// The Exchange Online sidecar of the tests: the real script (copied next to the test assembly by the build) in a
/// real pwsh child, with the fake ExchangeOnlineManagement module under tests/sidecar/modules as the only module it
/// may find. Nothing reaches a tenant: the fake module answers with made-up objects for contoso.onmicrosoft.com.
/// Linked into both test projects.
/// </summary>
internal static class SidecarFixtures
{
    /// <summary>The organization the fake module accepts.</summary>
    internal const string Organization = "contoso.onmicrosoft.com";

    /// <summary>The folder that holds the fake module: the repo's tests/sidecar/modules.</summary>
    internal static string ModuleRoot { get; } = FindModuleRoot();

    /// <summary>The pwsh of this machine (CI's runners carry one).</summary>
    internal static string Pwsh { get; } = Doctor.FindPwsh() ?? throw new InvalidOperationException("the sidecar tests need pwsh on PATH");

    /// <summary>How to start the sidecar against the fake module (or, with <paramref name="moduleRoot"/>, another folder).</summary>
    internal static SidecarStartInfo StartInfo(string? moduleRoot = null) =>
        new(Pwsh, SidecarScript.InstalledPath)
        {
            Environment = new Dictionary<string, string> { ["M365AEGIS_EXO_MODULE_ROOT"] = moduleRoot ?? ModuleRoot },
        };

    /// <summary>A made-up token the fake module accepts; <paramref name="accessToken"/> picks one it refuses.</summary>
    internal static ExchangeToken Token(DateTimeOffset expiresOn, string accessToken = "fake-token") =>
        new(accessToken, expiresOn, ["Exchange.Manage", "Exchange.ManageV2"], "alice@contoso.com", "00000000-0000-0000-0000-000000000002");

    /// <summary>A session over the fake module, with the given token source, target and clock.</summary>
    internal static ExchangeSession Session(
        Func<CancellationToken, Task<ExchangeToken>>? token = null,
        ExchangeTarget? target = null,
        TimeProvider? clock = null,
        string? moduleRoot = null) =>
        new(new ExchangeSessionOptions
        {
            Start = cancellationToken => SidecarClient.StartAsync(StartInfo(moduleRoot), cancellationToken),
            Token = token ?? (_ => Task.FromResult(Token(DateTimeOffset.UtcNow.AddHours(1)))),
            Target = _ => Task.FromResult(target ?? new ExchangeTarget(Organization, null)),
            Clock = clock ?? TimeProvider.System,
        });

    private static string FindModuleRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            string candidate = Path.Combine(directory.FullName, "tests", "sidecar", "modules");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException($"no tests/sidecar/modules above {AppContext.BaseDirectory}");
    }
}
