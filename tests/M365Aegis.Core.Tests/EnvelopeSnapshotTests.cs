using M365Aegis.Core.Diagnostics;
using M365Aegis.Core.Output;
using M365Aegis.Tests;

namespace M365Aegis.Core.Tests;

/// <summary>
/// The envelope of the scrubbed samples, byte for byte (Snapshots/*.verified.json): a change here is a change of
/// the contract and is accepted on purpose, by renaming the received file.
/// </summary>
public sealed class EnvelopeSnapshotTests
{
    [Fact]
    public Task AHealthyDoctorReport()
    {
        CommandResult<DoctorReport> result = DoctorSamples.Healthy.ToResult();

        Assert.Equal(ExitCode.Ok, result.ExitCode);
        return Verify(EnvelopeJson.Serialize(result.ToEnvelope(DoctorSamples.LocalMeta)), extension: "json");
    }

    [Fact]
    public Task AFailedDoctorReportCarriesThePartialData()
    {
        CommandResult<DoctorReport> result = DoctorSamples.PwshMissing.ToResult();

        Assert.Equal(ExitCode.DoctorFailed, result.ExitCode);
        return Verify(EnvelopeJson.Serialize(result.ToEnvelope(DoctorSamples.LocalMeta)), extension: "json");
    }

    [Fact]
    public Task AFailureWithoutData()
    {
        var failure = new AegisFailure(FailureKind.NotFound, "no group named 'Finance'", "list the groups with `m365-aegis group list`");
        CommandResult<object> result = CommandResult.Failure<object>(failure);

        Assert.Equal(ExitCode.NotFound, result.ExitCode);
        return Verify(EnvelopeJson.Serialize(result.ToEnvelope(DoctorSamples.GraphMeta)), extension: "json");
    }
}
