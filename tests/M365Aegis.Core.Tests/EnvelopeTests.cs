using M365Aegis.Core.Output;

namespace M365Aegis.Core.Tests;

public sealed class EnvelopeTests
{
    private static readonly EnvelopeMeta Meta = new(["req-1"], 12, "local");

    [Fact]
    public void SuccessCarriesDataAndNoError()
    {
        string json = EnvelopeJson.Serialize(Envelope.Success("hello", Meta));

        Assert.Equal("""{"ok":true,"data":"hello","meta":{"requestIds":["req-1"],"elapsedMs":12,"backend":"local"}}""", json);
    }

    [Fact]
    public void FailureCarriesErrorAndNoData()
    {
        var error = new EnvelopeError("not-found", "no such group", "check the name");
        string json = EnvelopeJson.Serialize(Envelope.Failure<string>(error, Meta));

        Assert.Equal(
            """{"ok":false,"error":{"code":"not-found","message":"no such group","hint":"check the name"},"meta":{"requestIds":["req-1"],"elapsedMs":12,"backend":"local"}}""",
            json);
    }

    [Fact]
    public void AccentedTextStaysReadable()
    {
        string json = EnvelopeJson.Serialize(Envelope.Success("Árvíztűrő tükörfúrógép", Meta));

        Assert.Contains("Árvíztűrő tükörfúrógép", json, StringComparison.Ordinal);
    }

    [Fact]
    public void ExitCodesAreTheContract()
    {
        Assert.Equal(0, (int)ExitCode.Ok);
        Assert.Equal(2, (int)ExitCode.Usage);
        Assert.Equal(3, (int)ExitCode.Auth);
        Assert.Equal(4, (int)ExitCode.NotFound);
        Assert.Equal(5, (int)ExitCode.Throttled);
        Assert.Equal(6, (int)ExitCode.Partial);
        Assert.Equal(7, (int)ExitCode.RefusedWithoutYes);
        Assert.Equal(10, (int)ExitCode.DoctorFailed);
    }
}
