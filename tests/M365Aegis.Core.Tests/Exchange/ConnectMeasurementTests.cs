using M365Aegis.Core.Exchange;
using M365Aegis.Core.Exchange.Sidecar;
using M365Aegis.Core.Output;
using M365Aegis.Tests;

namespace M365Aegis.Core.Tests.Exchange;

/// <summary>The connect measurement of task 011 against the fake module: fresh sidecars, one token, three variants.</summary>
public sealed class ConnectMeasurementTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task EveryVariantRunsInFreshSidecarsWithOneToken()
    {
        int tokens = 0;
        int starts = 0;
        var session = new ExchangeSessionOptions
        {
            Start = cancellationToken =>
            {
                starts++;
                return SidecarClient.StartAsync(SidecarFixtures.StartInfo(), cancellationToken);
            },
            Token = _ =>
            {
                tokens++;
                return Task.FromResult(SidecarFixtures.Token(DateTimeOffset.UtcNow.AddHours(1)));
            },
            Target = _ => Task.FromResult(new ExchangeTarget(SidecarFixtures.Organization, null)),
        };

        ConnectMeasurementReport report = await ConnectMeasurement.RunAsync(
            session, [ConnectVariant.Default, ConnectVariant.SkipFormatData, ConnectVariant.CommandName], runs: 2, Ct);

        Assert.Equal(1, tokens);
        Assert.Equal(6, starts);
        Assert.Equal(6, report.Runs.Count);
        Assert.All(report.Runs, run => Assert.Null(run.Error));
        Assert.Equal([ConnectVariant.Default, ConnectVariant.SkipFormatData, ConnectVariant.CommandName], report.Summary.Select(summary => summary.Variant));
        Assert.All(report.Summary, summary =>
        {
            Assert.Equal(2, summary.Runs);
            Assert.Equal(0, summary.Failed);
            Assert.True(summary.AnswersAllowList);
            Assert.True(summary.Total.Median > 0);
        });
        Assert.Equal("3.10.1", report.Environment.Module);
        Assert.False(report.Environment.ModuleUnderOneDrive);
        Assert.Equal(ExoCmdlets.Allowed, report.Environment.AllowList);
    }

    [Fact]
    public async Task ARunThatCannotConnectIsCountedAsFailed()
    {
        var session = new ExchangeSessionOptions
        {
            Start = cancellationToken => SidecarClient.StartAsync(SidecarFixtures.StartInfo(), cancellationToken),
            Token = _ => Task.FromResult(SidecarFixtures.Token(DateTimeOffset.UtcNow.AddHours(1), "expired-token")),
            Target = _ => Task.FromResult(new ExchangeTarget(SidecarFixtures.Organization, null)),
        };

        ConnectMeasurementReport report = await ConnectMeasurement.RunAsync(session, [ConnectVariant.CommandName], runs: 1, Ct);

        ConnectRun run = Assert.Single(report.Runs);
        Assert.StartsWith("auth: ", run.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("expired-token", run.Error, StringComparison.Ordinal);
        VariantSummary summary = Assert.Single(report.Summary);
        Assert.Equal(1, summary.Failed);
        Assert.False(summary.AnswersAllowList);
        Assert.Null(summary.Total.Median);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(21)]
    public async Task TheRunsAreOneToTwenty(int runs)
    {
        var session = new ExchangeSessionOptions
        {
            Start = _ => throw new InvalidOperationException("no start expected"),
            Token = _ => throw new InvalidOperationException("no token expected"),
            Target = _ => throw new InvalidOperationException("no target expected"),
        };

        AegisException exception = await Assert.ThrowsAsync<AegisException>(() => ConnectMeasurement.RunAsync(session, [ConnectVariant.Default], runs, Ct));

        Assert.Equal(FailureKind.Usage, exception.Failure.Kind);
    }

    [Theory]
    [InlineData("default", ConnectVariant.Default)]
    [InlineData(" Skip-Format-Data ", ConnectVariant.SkipFormatData)]
    [InlineData("command-name", ConnectVariant.CommandName)]
    public void AVariantIsReadByItsName(string name, ConnectVariant variant)
    {
        Assert.Equal(variant, ConnectMeasurement.ParseVariant(name));
        Assert.Equal(name.Trim().ToLowerInvariant(), ConnectMeasurement.Name(variant));
    }

    [Theory]
    [InlineData(@"C:\Users\someone\OneDrive - Contoso\Documents\PowerShell\Modules", true)]
    [InlineData(@"C:\Users\someone\OneDrive\Documents", true)]
    [InlineData(@"D:\Synced\Documents\PowerShell\Modules", true)]
    [InlineData(@"C:\Users\someone\Documents\PowerShell\Modules", false)]
    [InlineData(@"C:\Program Files\PowerShell\Modules", false)]
    [InlineData("/home/someone/.local/share/powershell/Modules", false)]
    [InlineData(null, false)]
    public void APathIsUnderOneDriveByItsRootOrItsFolderName(string? path, bool under) =>
        Assert.Equal(under, ConnectMeasurement.UnderOneDrive(path, [@"D:\Synced"]));

    [Fact]
    public void TheMedianAndTheSpread()
    {
        Assert.Equal(new Spread(3, 1, 9), ConnectMeasurement.SpreadOf([9, 1, 3]));
        Assert.Equal(new Spread(3, 1, 9), ConnectMeasurement.SpreadOf([9, 1, 2, 4]));
        Assert.Equal(new Spread(null, null, null), ConnectMeasurement.SpreadOf([]));
    }
}
