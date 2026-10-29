using System.Text.Json;
using M365Aegis.Core.Diagnostics;
using M365Aegis.Core.Output;

namespace M365Aegis.Tests;

/// <summary>
/// The scrubbed doctor reports under tests/fixtures/doctor, read back with the contract's own JSON options, and
/// a fixed meta, so the snapshot tests of the Core and the Cli render the same samples. Linked into both test
/// projects (a <c>Compile</c> item), which is why it has its own namespace.
/// </summary>
internal static class DoctorSamples
{
    /// <summary>A meta with no request ids and a fixed elapsed time, as a doctor run would have.</summary>
    internal static EnvelopeMeta LocalMeta { get; } = new([], 12, Backend.Local);

    /// <summary>A meta of a Graph call, with one request id of the made-up form.</summary>
    internal static EnvelopeMeta GraphMeta { get; } = new(["00000000-0000-0000-0000-000000000001"], 345, Backend.Graph);

    /// <summary>Every check passes or is skipped; the report is healthy.</summary>
    internal static DoctorReport Healthy => Read("healthy.json");

    /// <summary>The required pwsh check fails; the report is not healthy.</summary>
    internal static DoctorReport PwshMissing => Read("pwsh-missing.json");

    private static DoctorReport Read(string fileName)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "fixtures", "doctor", fileName);
        return JsonSerializer.Deserialize<DoctorReport>(File.ReadAllText(path), EnvelopeJson.Options)
            ?? throw new InvalidOperationException($"{path} holds no doctor report");
    }
}
