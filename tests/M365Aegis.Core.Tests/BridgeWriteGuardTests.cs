using System.Reflection;
using System.Text.Json.Nodes;
using M365Aegis.Core.Bridge;

namespace M365Aegis.Core.Tests;

/// <summary>
/// The write guard over the window's bridge (task 079, D-006, SECURITY.md "Writes"), the window's counterpart of the
/// command line's test of task 071. The write requests come from the protocol itself: every type of
/// <see cref="BridgeMessageType"/> marked <see cref="BridgeWriteAttribute"/> (<see cref="BridgeMessageType.Writes"/>),
/// each planned from its sample under tests/contracts/bridge, so a new write request joins the test by its mark and its
/// sample, which the contract wants for every type anyway. Each one is driven without a confirmation over every layer
/// at once (the recorded Graph and Admin API answers, whose fixture transport writes each request to a log, and the
/// fake Exchange Online module, which writes each write cmdlet to a file): the plan, then every apply that is not a
/// confirmed apply of a plan the window holds. A run passes when the plan has a change to send, every such apply is
/// refused, the log holds nothing but reads, the fake module's file stays empty and the home folder's <c>plans</c>
/// holds no record. Beside it, every message of the protocol is sent from its sample, so a request that writes without
/// being marked, or answers with a plan without the mark, fails the test too.
/// </summary>
public sealed class BridgeWriteGuardTests
{
    /// <summary>The write requests of the protocol, by their mark.</summary>
    public static TheoryData<string> WriteRequests => [.. BridgeMessageType.Writes];

    /// <summary>Every message type the protocol names, read from <see cref="BridgeMessageType"/>.</summary>
    private static IEnumerable<string> AllTypes => typeof(BridgeMessageType)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(member => member.IsLiteral && member.FieldType == typeof(string))
        .Select(member => (string)member.GetRawConstantValue()!);

    [Fact]
    public void TheProtocolMarksWriteRequests()
    {
        Assert.NotEmpty(BridgeMessageType.Writes);
        Assert.Subset(new HashSet<string>(AllTypes, StringComparer.Ordinal), new HashSet<string>(BridgeMessageType.Writes, StringComparer.Ordinal));
    }

    [Fact]
    public void EveryMessageTypeHasItsSampleNamedAfterIt()
    {
        foreach (string type in AllTypes)
        {
            string path = Path.Combine(BridgeWindow.Contracts, type + ".json");
            Assert.True(File.Exists(path), $"'{type}' has no sample {type}.json under tests/contracts/bridge");
            Assert.Equal(type, BridgeWindow.Sample(type).Type);
        }
    }

    [Theory]
    [MemberData(nameof(WriteRequests))]
    public async Task AWriteRequestWithoutAConfirmationSendsNothing(string type)
    {
        await using var window = new BridgeWindow();
        BridgeMessage request = BridgeWindow.Sample(type);

        BridgeMessage plan = await window.SendAsync(request);
        Assert.Equal(BridgeMessageType.Plan, plan.Type);
        Assert.Equal("dry-run", plan.Data?["plan"]?["mode"]?.GetValue<string>());
        Assert.NotEmpty(plan.Data!["plan"]!["changes"]!.AsArray());
        string planId = BridgeWindow.PlanId(plan);

        // The same write asked for again, and every apply that is not a confirmed apply of a plan the window holds.
        string again = BridgeWindow.PlanId(await window.SendAsync(request));
        BridgeMessage[] applies =
        [
            BridgeWindow.Apply(planId, null),
            BridgeWindow.Apply(planId, false),
            BridgeWindow.Apply(planId, JsonValue.Create("true")),
            BridgeWindow.Apply(planId, JsonValue.Create(1)),
            BridgeWindow.Apply(null, true),
            BridgeWindow.Apply(planId.ToUpperInvariant(), true),
            BridgeWindow.Apply(Guid.NewGuid().ToString("N"), true),
            new BridgeMessage(BridgeProtocol.Version, "3", BridgeMessageType.ApplyPlan, null),
            new BridgeMessage(BridgeProtocol.Version, "4", BridgeMessageType.ApplyPlan, new JsonObject { ["plan"] = planId, ["yes"] = true }),
        ];
        foreach (BridgeMessage apply in applies)
        {
            BridgeMessage refused = await window.SendAsync(apply);
            Assert.True(refused.Type == "error", $"{BridgeProtocol.Write(apply)} was answered with {BridgeProtocol.Write(refused)}");
        }

        AssertNothingSent(window);

        // A plan whose time ran out, and one a sign-out dropped, are refused even when confirmed.
        window.Clock.Advance(BridgeDispatcher.PlanLifetime + TimeSpan.FromSeconds(1));
        Assert.Equal("error", (await window.SendAsync(BridgeWindow.Apply(planId, true))).Type);
        string dropped = BridgeWindow.PlanId(await window.SendAsync(request));
        Assert.Equal("logged-out", (await window.SendAsync(BridgeWindow.Sample(BridgeMessageType.Logout))).Type);
        Assert.Equal("error", (await window.SendAsync(BridgeWindow.Apply(dropped, true))).Type);
        Assert.Equal("error", (await window.SendAsync(BridgeWindow.Apply(again, true))).Type);

        AssertNothingSent(window);
    }

    [Fact]
    public async Task NoMessageOfTheProtocolWritesWithoutAConfirmation()
    {
        await using var window = new BridgeWindow();
        var planned = new List<string>();

        foreach (string type in AllTypes)
        {
            BridgeMessage? reply = await window.Dispatcher.DispatchAsync(BridgeWindow.Sample(type), TestContext.Current.CancellationToken);
            if (reply?.Type == BridgeMessageType.Plan)
            {
                planned.Add(type);
            }
        }

        // A request answered with a plan is a write request, and carries the mark that puts it into the test above.
        Assert.All(planned, type => Assert.True(BridgeMessageType.Writes.Contains(type), $"'{type}' answers with a plan but is not marked [BridgeWrite]"));
        Assert.NotEmpty(planned);
        AssertNothingSent(window);
    }

    /// <summary>Nothing but reads reached a layer, and no plan record was written.</summary>
    private static void AssertNothingSent(BridgeWindow window)
    {
        List<string> sent = window.Sent();
        Assert.True(sent.Count == 0, $"a write reached a layer without a confirmation: {string.Join("; ", sent)}");
        Assert.True(window.Records.Length == 0, $"a plan record was written without a confirmation: {string.Join("; ", window.Records)}");
    }
}
