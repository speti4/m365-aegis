using M365Aegis.Core.Output;

namespace M365Aegis.Core.Tests;

/// <summary>The failure kinds against the exit codes and error codes of D-006: a wrong code fails here.</summary>
public sealed class FailureTests
{
    [Theory]
    [InlineData(FailureKind.Usage, 2, "usage")]
    [InlineData(FailureKind.Auth, 3, "auth")]
    [InlineData(FailureKind.NotFound, 4, "not-found")]
    [InlineData(FailureKind.Throttled, 5, "throttled")]
    [InlineData(FailureKind.Partial, 6, "partial")]
    [InlineData(FailureKind.RefusedWithoutYes, 7, "refused-without-yes")]
    [InlineData(FailureKind.DoctorFailed, 10, "doctor-failed")]
    [InlineData(FailureKind.Internal, 70, "internal")]
    public void EveryKindHasItsExitCodeAndErrorCode(FailureKind kind, int exitCode, string code)
    {
        var failure = new AegisFailure(kind, "a sentence");

        Assert.Equal(exitCode, (int)failure.ExitCode);
        Assert.Equal(code, failure.Code);
        Assert.Equal(code, failure.ToError().Code);
    }

    [Fact]
    public void EveryKindIsMappedAndNoCodeIsShared()
    {
        FailureKind[] kinds = Enum.GetValues<FailureKind>();

        Assert.Equal(8, kinds.Length);
        Assert.Equal(kinds.Length, kinds.Select(kind => kind.ToExitCode()).Distinct().Count());
        Assert.Equal(kinds.Length, kinds.Select(kind => kind.ToCode()).Distinct().Count());
        Assert.All(kinds, kind => Assert.NotEqual(ExitCode.Ok, kind.ToExitCode()));
    }

    [Fact]
    public void AnUnknownKindIsRefused()
    {
        var kind = (FailureKind)99;

        Assert.Throws<ArgumentOutOfRangeException>(() => kind.ToExitCode());
        Assert.Throws<ArgumentOutOfRangeException>(() => kind.ToCode());
    }

    [Fact]
    public void ASuccessfulResultExitsWithZero()
    {
        CommandResult<string> result = CommandResult.Success("hello");

        Assert.True(result.Ok);
        Assert.Equal(ExitCode.Ok, result.ExitCode);
        Envelope<string> envelope = result.ToEnvelope(new EnvelopeMeta([], 1, Backend.Local));
        Assert.True(envelope.Ok);
        Assert.Equal("hello", envelope.Data);
        Assert.Null(envelope.Error);
    }

    [Fact]
    public void AFailedResultExitsWithTheFailuresCodeAndKeepsThePartialData()
    {
        var failure = new AegisFailure(FailureKind.Partial, "2 of 3 items failed");
        CommandResult<string[]> result = CommandResult.Failure<string[]>(failure, ["one"]);

        Assert.False(result.Ok);
        Assert.Equal(ExitCode.Partial, result.ExitCode);
        Envelope<string[]> envelope = result.ToEnvelope(new EnvelopeMeta([], 1, Backend.Graph));
        Assert.False(envelope.Ok);
        Assert.NotNull(envelope.Data);
        Assert.Equal("one", Assert.Single(envelope.Data));
        Assert.Equal("partial", envelope.Error?.Code);
    }

    [Fact]
    public void TheCoresExceptionBecomesTheFailedResult()
    {
        var exception = new AegisException(FailureKind.Auth, "no usable sign-in", "run `m365-aegis login`");

        CommandResult<int> result = CommandResult.Failure<int>(exception);

        Assert.Equal(ExitCode.Auth, result.ExitCode);
        Assert.Equal("no usable sign-in", exception.Message);
        Assert.Equal("run `m365-aegis login`", result.Failure?.Hint);
    }

    [Fact]
    public void TheEnvelopeWritesEnumsAsKebabCaseStrings()
    {
        string json = EnvelopeJson.Serialize(Envelope.Success(FailureKind.RefusedWithoutYes, new EnvelopeMeta([], 1, Backend.Local)));

        Assert.Contains("\"data\":\"refused-without-yes\"", json, StringComparison.Ordinal);
    }
}
