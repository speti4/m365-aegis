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
