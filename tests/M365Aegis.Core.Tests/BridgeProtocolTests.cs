using System.Text.Json.Nodes;
using M365Aegis.Core.Bridge;

namespace M365Aegis.Core.Tests;

public sealed class BridgeProtocolTests
{
    private static readonly string Contracts = Path.Combine(AppContext.BaseDirectory, "contracts", "bridge");

    [Fact]
    public void PingIsAnsweredWithPongAndTheVersion()
    {
        BridgeMessage? request = BridgeProtocol.TryParse(File.ReadAllText(Path.Combine(Contracts, "ping.json")));
        Assert.NotNull(request);

        BridgeMessage? reply = BridgeDispatcher.Dispatch(request);

        Assert.NotNull(reply);
        Assert.Equal("pong", reply.Type);
        Assert.Equal(request.Id, reply.Id);
        Assert.Equal(ProductInfo.Version, reply.Data?["version"]?.GetValue<string>());
        Assert.Equal(ProductInfo.Name, reply.Data?["product"]?.GetValue<string>());
    }

    [Fact]
    public void ReadyIsANotificationWithNoAnswer()
    {
        BridgeMessage? ready = BridgeProtocol.TryParse(File.ReadAllText(Path.Combine(Contracts, "ready.json")));
        Assert.NotNull(ready);
        Assert.Null(ready.Id);

        Assert.Null(BridgeDispatcher.Dispatch(ready));
    }

    [Fact]
    public void AnUnknownTypeIsAnError()
    {
        var request = new BridgeMessage(BridgeProtocol.Version, "7", "frobnicate", null);

        BridgeMessage? reply = BridgeDispatcher.Dispatch(request);

        Assert.NotNull(reply);
        Assert.Equal("error", reply.Type);
        Assert.Equal("unknown-type", reply.Data?["code"]?.GetValue<string>());
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("""{"aegis":2,"type":"ping"}""")]
    [InlineData("""{"type":"ping"}""")]
    [InlineData("""{"aegis":1}""")]
    public void ForeignTextIsRefused(string text)
    {
        Assert.Null(BridgeProtocol.TryParse(text));
    }

    [Fact]
    public void AMessageRoundTrips()
    {
        var message = new BridgeMessage(BridgeProtocol.Version, "42", "pong", new JsonObject { ["version"] = "1.2.3" });

        string json = BridgeProtocol.Write(message);
        BridgeMessage? parsed = BridgeProtocol.TryParse(json);

        Assert.Equal("""{"aegis":1,"id":"42","type":"pong","data":{"version":"1.2.3"}}""", json);
        Assert.NotNull(parsed);
        Assert.Equal(message.Type, parsed.Type);
        Assert.Equal(message.Id, parsed.Id);
    }
}
