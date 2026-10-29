using System.Text.Json;
using M365Aegis.Cli.Commands;
using M365Aegis.Cli.Output;
using M365Aegis.Core.Diagnostics;
using M365Aegis.Core.Output;
using M365Aegis.Tests;

namespace M365Aegis.Cli.Tests;

/// <summary>
/// The one rendering path over the scrubbed samples, in process and byte for byte (Snapshots/*.verified.txt):
/// the table on stdout, the error line on stderr, the envelope when asked. The exit code is asserted on every
/// rendering, so a wrong one fails here.
/// </summary>
public sealed class RendererSnapshotTests
{
    [Fact]
    public Task AHealthyDoctorTable()
    {
        (ExitCode exitCode, string rendering) = Render(DoctorSamples.Healthy.ToResult(), OutputMode.Table);

        Assert.Equal(ExitCode.Ok, exitCode);
        return Verify(rendering);
    }

    [Fact]
    public Task AFailedDoctorTableEndsWithTheErrorOnStderr()
    {
        (ExitCode exitCode, string rendering) = Render(DoctorSamples.PwshMissing.ToResult(), OutputMode.Table);

        Assert.Equal(ExitCode.DoctorFailed, exitCode);
        return Verify(rendering);
    }

    [Fact]
    public Task AHealthyDoctorEnvelopeThroughTheRenderer()
    {
        (ExitCode exitCode, string rendering) = Render(DoctorSamples.Healthy.ToResult(), OutputMode.Json);

        Assert.Equal(ExitCode.Ok, exitCode);
        return Verify(rendering);
    }

    [Fact]
    public Task AUsageErrorHasNoTableOnlyTheErrorLine()
    {
        var failure = new AegisFailure(FailureKind.Usage, "Unrecognized command or argument 'frobnicate'.", "see 'm365-aegis --help'");
        var output = new StringWriter();
        var error = new StringWriter();

        ExitCode exitCode = Renderer.Render(
            CommandResult.Failure<object>(failure), DoctorSamples.LocalMeta, NoTableView<object>.Instance, OutputMode.Table, output, error);

        Assert.Equal(ExitCode.Usage, exitCode);
        Assert.Equal("", output.ToString());
        return Verify(Combine(output, error));
    }

    [Fact]
    public Task ASignedInUserTable()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        ExitCode exitCode = Renderer.Render(
            CommandResult.Success(AuthSamples.SignedIn), DoctorSamples.LocalMeta, SignInTableView.Instance, OutputMode.Table, output, error);

        Assert.Equal(ExitCode.Ok, exitCode);
        return Verify(Combine(output, error));
    }

    [Fact]
    public Task AMissingExchangeTokenIsARowOfTheTable()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        ExitCode exitCode = Renderer.Render(
            CommandResult.Success(AuthSamples.NoExchangeToken), DoctorSamples.LocalMeta, SignInTableView.Instance, OutputMode.Table, output, error);

        Assert.Equal(ExitCode.Ok, exitCode);
        return Verify(Combine(output, error));
    }

    [Fact]
    public Task ASignOutTable()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        ExitCode exitCode = Renderer.Render(
            CommandResult.Success(AuthSamples.SignedOut), DoctorSamples.LocalMeta, SignOutTableView.Instance, OutputMode.Table, output, error);

        Assert.Equal(ExitCode.Ok, exitCode);
        return Verify(Combine(output, error));
    }

    [Fact]
    public Task ADoctorTableWithWarningsHasAFixLinePerHint()
    {
        (ExitCode exitCode, string rendering) = Render(DoctorSamples.Warnings.ToResult(), OutputMode.Table);

        Assert.Equal(ExitCode.Ok, exitCode);
        return Verify(rendering);
    }

    [Fact]
    public async Task AnInternalErrorAtATerminalIsTheErrorLineWithTheLogFile()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        Exception? logged = null;

        int exitCode = await CommandRunner.RunAsync<DoctorReport>(
            OutputMode.Table,
            Backend.Local,
            () => throw new InvalidOperationException("boom for alice@contoso.com"),
            DoctorCommand.DoctorTableView.Instance,
            output,
            error,
            exception => { logged = exception; return LogFile; });

        Assert.Equal(70, exitCode);
        Assert.IsType<InvalidOperationException>(logged);
        await Verify(Combine(output, error));
    }

    [Fact]
    public async Task AnInternalErrorInAPipeIsAWellFormedEnvelopeWithoutTheStackTrace()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        int exitCode = await CommandRunner.RunAsync<DoctorReport>(
            OutputMode.Json,
            Backend.Local,
            () => Task.FromException<CommandResult<DoctorReport>>(new FormatException("a malformed value")),
            DoctorCommand.DoctorTableView.Instance,
            output,
            error,
            _ => LogFile);

        Assert.Equal((int)ExitCode.Internal, exitCode);
        Assert.Equal("", error.ToString());
        using JsonDocument document = JsonDocument.Parse(output.ToString());
        JsonElement root = document.RootElement;
        Assert.False(root.GetProperty("ok").GetBoolean());
        Assert.Equal("internal", root.GetProperty("error").GetProperty("code").GetString());
        Assert.Contains("FormatException", root.GetProperty("error").GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Contains(LogFile, root.GetProperty("error").GetProperty("hint").GetString(), StringComparison.Ordinal);
        Assert.DoesNotContain(" at ", output.ToString(), StringComparison.Ordinal);
        Assert.Equal("local", root.GetProperty("meta").GetProperty("backend").GetString());
    }

    [Fact]
    public async Task ACancellationIsNotAnInternalError()
    {
        await Assert.ThrowsAsync<OperationCanceledException>(() => CommandRunner.RunAsync<DoctorReport>(
            OutputMode.Json,
            Backend.Local,
            () => throw new OperationCanceledException(),
            DoctorCommand.DoctorTableView.Instance,
            new StringWriter(),
            new StringWriter(),
            _ => LogFile));
    }

    [Fact]
    public Task AnInternalErrorWhoseLogFailedSaysSo()
    {
        AegisFailure failure = CommandRunner.InternalFailure(new InvalidOperationException("boom"), logFile: null);
        var output = new StringWriter();
        var error = new StringWriter();

        ExitCode exitCode = Renderer.Render(
            CommandResult.Failure<object>(failure), DoctorSamples.LocalMeta, NoTableView<object>.Instance, OutputMode.Json, output, error);

        Assert.Equal(ExitCode.Internal, exitCode);
        return Verify(Combine(output, error));
    }

    [Fact]
    public Task AProfileListTable()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        ExitCode exitCode = Renderer.Render(
            CommandResult.Success(AuthSamples.Profiles), DoctorSamples.LocalMeta, ProfileCommand.ProfileListTableView.Instance, OutputMode.Table, output, error);

        Assert.Equal(ExitCode.Ok, exitCode);
        return Verify(Combine(output, error));
    }

    [Fact]
    public Task AProfileListWithoutADefaultSaysHowToPickOne()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        Renderer.Render(
            CommandResult.Success(AuthSamples.ProfilesWithoutDefault), DoctorSamples.LocalMeta, ProfileCommand.ProfileListTableView.Instance, OutputMode.Table, output, error);

        return Verify(Combine(output, error));
    }

    [Fact]
    public Task AProfileRemovalTable()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        ExitCode exitCode = Renderer.Render(
            CommandResult.Success(AuthSamples.RemovedProfile), DoctorSamples.LocalMeta, ProfileCommand.ProfileRemovalTableView.Instance, OutputMode.Table, output, error);

        Assert.Equal(ExitCode.Ok, exitCode);
        return Verify(Combine(output, error));
    }

    private const string LogFile = @"D:\aegis-home\logs\m365-aegis-2026-10-29.log";

    private static (ExitCode ExitCode, string Rendering) Render(CommandResult<DoctorReport> result, OutputMode mode)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        ExitCode exitCode = Renderer.Render(result, DoctorSamples.LocalMeta, DoctorCommand.DoctorTableView.Instance, mode, output, error);
        return (exitCode, Combine(output, error));
    }

    /// <summary>Both streams in one snapshot, each under its name, so the test shows what went where.</summary>
    private static string Combine(StringWriter output, StringWriter error) =>
        $"--- stdout ---\n{output}--- stderr ---\n{error}";
}
