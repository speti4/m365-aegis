using System.Runtime.CompilerServices;

namespace M365Aegis.Cli.Tests;

/// <summary>The Verify settings of this project: the snapshot files live under Snapshots/, named after the test.</summary>
internal static class ModuleInitializer
{
    [ModuleInitializer]
    internal static void Initialize() =>
        Verifier.DerivePathInfo((_, projectDirectory, type, method) => new(
            directory: Path.Combine(projectDirectory, "Snapshots"),
            typeName: type.Name,
            methodName: method.Name));
}
