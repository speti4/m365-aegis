using M365Aegis.Core.Auth;
using M365Aegis.Core.Config;
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

    [Fact]
    public Task ASignedInUser()
    {
        CommandResult<SignInInfo> result = CommandResult.Success(AuthSamples.SignedIn);

        Assert.Equal(ExitCode.Ok, result.ExitCode);
        return Verify(EnvelopeJson.Serialize(result.ToEnvelope(DoctorSamples.LocalMeta)), extension: "json");
    }

    [Fact]
    public Task ASignedInUserWithoutAnExchangeToken()
    {
        CommandResult<SignInInfo> result = CommandResult.Success(AuthSamples.NoExchangeToken);

        return Verify(EnvelopeJson.Serialize(result.ToEnvelope(DoctorSamples.LocalMeta)), extension: "json");
    }

    [Fact]
    public Task ANoSignInFailure()
    {
        var failure = new AegisFailure(FailureKind.Auth, "no silent sign-in for alice@contoso.com (operating system account: failed_to_acquire_token_silently_from_broker; alice@contoso.com is not among the 0 known account(s))", "run `m365-aegis login`");
        CommandResult<SignInInfo> result = CommandResult.Failure<SignInInfo>(failure);

        Assert.Equal(ExitCode.Auth, result.ExitCode);
        return Verify(EnvelopeJson.Serialize(result.ToEnvelope(DoctorSamples.LocalMeta)), extension: "json");
    }

    [Fact]
    public Task ADoctorReportWithWarningsAndTheirFixes()
    {
        CommandResult<DoctorReport> result = DoctorSamples.Warnings.ToResult();

        Assert.Equal(ExitCode.Ok, result.ExitCode);
        return Verify(EnvelopeJson.Serialize(result.ToEnvelope(DoctorSamples.LocalMeta)), extension: "json");
    }

    [Fact]
    public Task AnInternalError()
    {
        var failure = new AegisFailure(
            FailureKind.Internal,
            "an unexpected internal error stopped the command (InvalidOperationException: boom)",
            @"the stack trace is in D:\aegis-home\logs\m365-aegis-2026-10-29.log; report it with that log");
        CommandResult<object> result = CommandResult.Failure<object>(failure);

        Assert.Equal(ExitCode.Internal, result.ExitCode);
        return Verify(EnvelopeJson.Serialize(result.ToEnvelope(DoctorSamples.LocalMeta)), extension: "json");
    }

    [Fact]
    public Task AProfileList()
    {
        CommandResult<ProfileList> result = CommandResult.Success(AuthSamples.Profiles);

        return Verify(EnvelopeJson.Serialize(result.ToEnvelope(DoctorSamples.LocalMeta)), extension: "json");
    }

    [Fact]
    public Task AProfileRemoval()
    {
        CommandResult<ProfileRemoval> result = CommandResult.Success(AuthSamples.RemovedProfile);

        return Verify(EnvelopeJson.Serialize(result.ToEnvelope(DoctorSamples.LocalMeta)), extension: "json");
    }
}
