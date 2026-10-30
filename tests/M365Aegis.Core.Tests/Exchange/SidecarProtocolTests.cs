using System.Text.Json;
using M365Aegis.Core.Exchange.Sidecar;
using M365Aegis.Core.Output;
using M365Aegis.Tests;

namespace M365Aegis.Core.Tests.Exchange;

/// <summary>
/// The client side of the sidecar protocol against a scripted child: a pwsh script that answers each request with the
/// lines a test gives (replies of another id or version, lines that are no JSON, an error without data, a child that
/// exits, hangs or talks on stderr), which the real script never sends. Nothing reaches a tenant or a module.
/// </summary>
public sealed class SidecarProtocolTests
{
    private const string Hello = """{"aegis":1,"id":"{id}","type":"hello","data":{"pwsh":"7.6.0","module":"3.10.1","allowedCmdlets":[]}}""";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task LinesThatAreNotTheAnswerAreSkippedAndMissingValuesAreDefaults()
    {
        await using SidecarClient client = await ScriptedSidecar.StartAsync(
        [
            [
                "WARNING: not a reply",
                "[1]",
                """{"id":"{id}"}""",
                """{"aegis":"1","id":"{id}"}""",
                """{"aegis":2,"id":"{id}"}""",
                """{"aegis":1}""",
                """{"aegis":1,"id":"another"}""",
                """{"aegis":1,"id":"{id}","type":"hello","data":{"pwsh":7}}""",
            ],
            ["""{"aegis":1,"id":"{id}"}"""],
            ["""{"aegis":1,"id":"{id}","type":"connected","data":{"importMs":"soon"}}"""],
            ["""{"aegis":1,"id":"{id}","type":"invoked","data":5}"""],
        ]);

        Assert.Equal("?", client.Hello.Pwsh);
        Assert.Null(client.Hello.Module);
        Assert.Empty(client.Hello.AllowedCmdlets);

        ExoResult result = await client.InvokeAsync(new ExoCommand("Get-Mailbox"), Ct);
        Assert.Empty(result.Items);
        Assert.Empty(result.Errors);

        SidecarConnected connected = await client.ConnectAsync(new SidecarConnect("token", null, null), again: false, Ct);
        Assert.Equal(0, connected.ImportMs);
        Assert.Equal(0, connected.ConnectMs);
        Assert.Null(connected.Organization);
        Assert.Null(connected.Missing);

        Assert.Empty((await client.InvokeAsync(new ExoCommand("Get-Mailbox"), Ct)).Items);
    }

    [Fact]
    public async Task AnErrorReplyWithoutDataIsAnUnknownRefusal()
    {
        await using SidecarClient client = await ScriptedSidecar.StartAsync([[Hello], ["""{"aegis":1,"id":"{id}","type":"error"}"""]]);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => client.SendAsync("ping", null, TimeSpan.FromSeconds(30), Ct));

        Assert.Contains("(unknown: the sidecar reported an error without a message)", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFailedHelloEndsTheChildAndThrows()
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ScriptedSidecar.StartAsync([["""{"aegis":1,"id":"{id}","type":"error","data":{"code":"bad-request","message":"no hello here"}}"""]]));

        Assert.Contains("the sidecar refused 'hello' (bad-request: no hello here)", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AChildThatExitsWhileAnsweringIsUnavailable()
    {
        await using SidecarClient client = await ScriptedSidecar.StartAsync([[Hello], ["!exit"]]);

        AegisException exception = await Assert.ThrowsAsync<AegisException>(() => client.InvokeAsync(new ExoCommand("Get-Mailbox"), Ct));

        Assert.Equal(FailureKind.SidecarUnavailable, exception.Failure.Kind);
        Assert.Equal("the Exchange Online sidecar stopped while answering 'invoke'", exception.Failure.Message);
    }

    [Fact]
    public async Task AChildThatHangsTimesOutWithItsLastStderrLinesAndIsKilledOnDispose()
    {
        SidecarClient client = await ScriptedSidecar.StartAsync(
            [[Hello], ["!stderr:25", "!hang"]],
            start => start with { InvokeTimeout = TimeSpan.FromSeconds(3), ExitTimeout = TimeSpan.FromMilliseconds(200) },
            ignoreExit: true);

        AegisException exception = await Assert.ThrowsAsync<AegisException>(() => client.InvokeAsync(new ExoCommand("Get-Mailbox"), Ct));

        Assert.Equal(FailureKind.SidecarUnavailable, exception.Failure.Kind);
        Assert.StartsWith("the Exchange Online sidecar did not answer 'invoke' within 3 s; its last words: stderr line 6 | ", exception.Failure.Message, StringComparison.Ordinal);
        Assert.EndsWith("stderr line 25", exception.Failure.Message, StringComparison.Ordinal);
        Assert.Equal(20, client.StderrTail().Split(" | ").Length);

        await client.DisposeAsync();
        Assert.True(client.HasExited);
        await client.DisposeAsync();
    }

    [Fact]
    public async Task ASessionRefusesAScriptWhoseAllowListIsNotTheCores()
    {
        await using var session = new ExchangeSession(new ExchangeSessionOptions
        {
            Start = _ => ScriptedSidecar.StartAsync([[Hello]]),
            Token = _ => Task.FromResult(SidecarFixtures.Token(DateTimeOffset.UtcNow.AddHours(1))),
            Target = _ => Task.FromResult(new ExchangeTarget(SidecarFixtures.Organization, null)),
        });
        Assert.Null(session.Hello);

        AegisException exception = await Assert.ThrowsAsync<AegisException>(() => session.InvokeAsync(new ExoCommand(ExoCmdlets.Allowed[0]), Ct));

        Assert.Equal(FailureKind.SidecarUnavailable, exception.Failure.Kind);
        Assert.Equal("the sidecar script's allow-list differs from this build's", exception.Failure.Message);
        await session.DisposeAsync();
    }
}

/// <summary>
/// A write to a child that has ended, alone: run beside the other tests, the write sometimes reaches a pipe that is
/// still open (the read end outlives the child under load), and the answer is the end of the stream instead of the
/// failed write; both are the same failure of the contract, but only a run of its own reaches the failed write.
/// </summary>
[Collection(nameof(SidecarRunsAlone))]
public sealed class SidecarWriteTests
{
    [Fact]
    public async Task AWriteToAChildThatHasEndedIsUnavailable()
    {
        const string hello = """{"aegis":1,"id":"{id}","type":"hello","data":{"pwsh":"7.6.0","module":"3.10.1","allowedCmdlets":[]}}""";
        await using SidecarClient client = await ScriptedSidecar.StartAsync([[hello, "!exit"]]);
        await ScriptedSidecar.UntilExitedAsync(client);

        AegisException exception = await Assert.ThrowsAsync<AegisException>(() => client.InvokeAsync(new ExoCommand("Get-Mailbox"), TestContext.Current.CancellationToken));

        Assert.Equal(FailureKind.SidecarUnavailable, exception.Failure.Kind);
        Assert.StartsWith("the Exchange Online sidecar stopped while answering 'invoke' (", exception.Failure.Message, StringComparison.Ordinal);
    }
}

/// <summary>The tests that run after every other one, one at a time.</summary>
[CollectionDefinition(nameof(SidecarRunsAlone), DisableParallelization = true)]
public sealed class SidecarRunsAlone;

/// <summary>
/// A pwsh child that answers request N with the N-th list of lines (<c>{id}</c> is the request's id); <c>!exit</c>
/// ends it, <c>!hang</c> sleeps, <c>!stderr:N</c> writes N lines to stderr. With <c>ignoreExit</c> it sleeps on the
/// client's <c>exit</c> instead of ending, so the dispose has to kill it.
/// </summary>
internal static class ScriptedSidecar
{
    private const string Script = """
        $plan = Get-Content -Raw -LiteralPath $env:AEGIS_SCRIPTED_PLAN | ConvertFrom-Json -NoEnumerate
        $index = 0
        while ($null -ne ($line = [Console]::In.ReadLine())) {
            $request = $line | ConvertFrom-Json
            if ($request.type -eq 'exit') {
                if ($env:AEGIS_SCRIPTED_IGNORE_EXIT -eq '1') { Start-Sleep -Seconds 120 }
                break
            }
            $steps = if ($index -lt $plan.Count) { $plan[$index] } else { @() }
            $index++
            foreach ($step in $steps) {
                if ($step -eq '!exit') { exit 0 }
                elseif ($step -eq '!hang') { Start-Sleep -Seconds 120 }
                elseif ($step.StartsWith('!stderr:')) {
                    foreach ($n in 1..([int]$step.Substring(8))) { [Console]::Error.WriteLine("stderr line $n") }
                    [Console]::Error.Flush()
                }
                else { [Console]::Out.WriteLine($step.Replace('{id}', [string]$request.id)) }
            }
            [Console]::Out.Flush()
        }
        """;

    private static readonly Lazy<string> ScriptPath = new(() =>
    {
        string folder = Path.Combine(Path.GetTempPath(), "m365-aegis-tests", "scripted-sidecar", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "scripted-sidecar.ps1");
        File.WriteAllText(path, Script);
        return path;
    });

    internal static Task<SidecarClient> StartAsync(string[][] plan, Func<SidecarStartInfo, SidecarStartInfo>? tweak = null, bool ignoreExit = false)
    {
        string folder = Path.Combine(Path.GetTempPath(), "m365-aegis-tests", "scripted-sidecar", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string planPath = Path.Combine(folder, "plan.json");
        File.WriteAllText(planPath, JsonSerializer.Serialize(plan));
        var start = new SidecarStartInfo(SidecarFixtures.Pwsh, ScriptPath.Value)
        {
            Environment = new Dictionary<string, string>
            {
                ["AEGIS_SCRIPTED_PLAN"] = planPath,
                ["AEGIS_SCRIPTED_IGNORE_EXIT"] = ignoreExit ? "1" : "0",
            },
        };
        return SidecarClient.StartAsync(tweak is null ? start : tweak(start), TestContext.Current.CancellationToken);
    }

    internal static async Task UntilExitedAsync(SidecarClient client)
    {
        for (int wait = 0; wait < 200 && !client.HasExited; wait++)
        {
            await Task.Delay(50, TestContext.Current.CancellationToken);
        }

        Assert.True(client.HasExited);
    }
}
