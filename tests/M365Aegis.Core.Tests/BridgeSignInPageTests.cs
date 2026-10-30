using System.Text.Json.Nodes;
using M365Aegis.Core.Auth;
using M365Aegis.Core.Bridge;
using M365Aegis.Core.Config;
using M365Aegis.Core.Diagnostics;
using M365Aegis.Core.Graph;
using M365Aegis.Core.Tests.Auth;
using M365Aegis.Core.Tests.Config;
using M365Aegis.Tests;

namespace M365Aegis.Core.Tests;

/// <summary>
/// The device-code card's "Open in browser" (task 047): the host hands the browser only the page its own device-code
/// notification carried, only while that sign-in waits, and only on a Microsoft sign-in host over https; the request
/// and its reply are the samples under tests/contracts/bridge.
/// </summary>
public sealed class BridgeSignInPageTests
{
    private static readonly string Contracts = Path.Combine(AppContext.BaseDirectory, "contracts", "bridge");
    private static readonly CancellationToken None = CancellationToken.None;

    [Theory]
    [InlineData("https://login.microsoftonline.com/common/oauth2/deviceauth", true)]
    [InlineData("https://microsoft.com/devicelogin", true)]
    [InlineData("https://www.microsoft.com/devicelogin", true)]
    [InlineData("https://login.microsoft.com/device", true)]
    [InlineData("https://LOGIN.MICROSOFTONLINE.COM/common/oauth2/deviceauth", true)]
    [InlineData("http://microsoft.com/devicelogin", false)]
    [InlineData("https://microsoft.com:8443/devicelogin", false)]
    [InlineData("https://user@microsoft.com/devicelogin", false)]
    [InlineData("https://microsoft.com.example/devicelogin", false)]
    [InlineData("https://evilmicrosoft.com/devicelogin", false)]
    [InlineData("https://login.microsoftonline.com.contoso.com/", false)]
    [InlineData("https://aka.ms/devicelogin", false)]
    [InlineData("file:///C:/Windows/System32/calc.exe", false)]
    [InlineData("javascript:alert(1)", false)]
    public void OnlyAMicrosoftSignInPageOverHttpsIsAllowed(string page, bool allowed)
    {
        Assert.Equal(allowed, SignInPage.IsAllowed(new Uri(page)));
    }

    [Fact]
    public void ARelativeOrMissingAddressIsNotAllowed()
    {
        Assert.False(SignInPage.IsAllowed(null));
        Assert.False(SignInPage.IsAllowed(new Uri("/common/oauth2/deviceauth", UriKind.Relative)));
    }

    [Fact]
    public async Task WhileADeviceCodeSignInWaitsTheHostOpensItsPage()
    {
        var opened = new List<Uri>();
        BridgeMessage? openReply = null;
        BridgeDispatcher? dispatcher = null;
        var broker = new FakeTokenBroker
        {
            DeviceCode = scope => FakeTokenBroker.Token(scope, FakeTokenBroker.Alice, TokenOrigin.IdentityProvider),
            Silent = (scope, account) => FakeTokenBroker.Token(scope, account, TokenOrigin.Cache),
            WhileTheCodeIsShown = async () => openReply = await dispatcher!.DispatchAsync(Sample("open-sign-in-page.json"), None),
        };
        dispatcher = Dispatcher(broker, opened);

        BridgeMessage? login = await dispatcher.DispatchAsync(Sample("login.json") with { Data = new JsonObject { ["deviceCode"] = true } }, _ => Task.CompletedTask, None);

        Assert.Equal("signed-in", login?.Type);
        AssertIsSample("sign-in-page-opened.json", openReply);
        Assert.Equal([new Uri("https://login.microsoftonline.com/common/oauth2/deviceauth")], opened);
    }

    [Fact]
    public async Task WithoutASignInWaitingNothingIsOpened()
    {
        var opened = new List<Uri>();
        BridgeDispatcher dispatcher = Dispatcher(new FakeTokenBroker(), opened);

        BridgeMessage? before = await dispatcher.DispatchAsync(Sample("open-sign-in-page.json"), None);
        var broker = new FakeTokenBroker
        {
            DeviceCode = scope => FakeTokenBroker.Token(scope, FakeTokenBroker.Alice, TokenOrigin.IdentityProvider),
            Silent = (scope, account) => FakeTokenBroker.Token(scope, account, TokenOrigin.Cache),
        };
        BridgeDispatcher signedIn = Dispatcher(broker, opened);
        await signedIn.DispatchAsync(Sample("login.json") with { Data = new JsonObject { ["deviceCode"] = true } }, _ => Task.CompletedTask, None);
        BridgeMessage? after = await signedIn.DispatchAsync(Sample("open-sign-in-page.json"), None);

        Assert.Equal("usage", before?.Data?["code"]?.GetValue<string>());
        Assert.Equal("usage", after?.Data?["code"]?.GetValue<string>());
        Assert.Empty(opened);
    }

    [Fact]
    public async Task APageOffMicrosoftsSignInHostsIsNeverOpened()
    {
        var opened = new List<Uri>();
        BridgeMessage? openReply = null;
        BridgeDispatcher? dispatcher = null;
        var broker = new FakeTokenBroker
        {
            DeviceCodePage = new Uri("https://login.contoso.com/devicelogin"),
            WhileTheCodeIsShown = async () => openReply = await dispatcher!.DispatchAsync(Sample("open-sign-in-page.json"), None),
        };
        dispatcher = Dispatcher(broker, opened);

        await dispatcher.DispatchAsync(Sample("login.json") with { Data = new JsonObject { ["deviceCode"] = true } }, _ => Task.CompletedTask, None);

        Assert.Equal("error", openReply?.Type);
        Assert.Equal("service-error", openReply?.Data?["code"]?.GetValue<string>());
        Assert.Contains("https://login.contoso.com", openReply?.Data?["message"]?.GetValue<string>(), StringComparison.Ordinal);
        Assert.Empty(opened);
    }

    [Fact]
    public async Task AWindowWithoutABrowserSaysSo()
    {
        BridgeMessage? openReply = null;
        BridgeDispatcher? dispatcher = null;
        var broker = new FakeTokenBroker
        {
            WhileTheCodeIsShown = async () => openReply = await dispatcher!.DispatchAsync(Sample("open-sign-in-page.json"), None),
        };
        dispatcher = Dispatcher(broker, opened: null);

        await dispatcher.DispatchAsync(Sample("login.json") with { Data = new JsonObject { ["deviceCode"] = true } }, _ => Task.CompletedTask, None);

        Assert.Equal("service-error", openReply?.Data?["code"]?.GetValue<string>());
        Assert.Equal("this window cannot open a browser", openReply?.Data?["message"]?.GetValue<string>());
    }

    private static BridgeMessage Sample(string fileName) =>
        BridgeProtocol.TryParse(File.ReadAllText(Path.Combine(Contracts, fileName)))
        ?? throw new InvalidOperationException($"{fileName} is not a message of the protocol");

    private static void AssertIsSample(string fileName, BridgeMessage? reply)
    {
        Assert.NotNull(reply);
        JsonNode expected = JsonNode.Parse(File.ReadAllText(Path.Combine(Contracts, fileName)))!;
        JsonNode actual = JsonNode.Parse(BridgeProtocol.Write(reply))!;
        Assert.True(JsonNode.DeepEquals(expected, actual), $"{fileName} differs from the reply:\n{actual.ToJsonString()}");
    }

    /// <summary>The window's dispatcher over a scripted sign-in; <paramref name="opened"/> collects what the browser was handed, null for no browser.</summary>
    private static BridgeDispatcher Dispatcher(FakeTokenBroker broker, List<Uri>? opened)
    {
        string home = ProfileStoreTests.TempHome();
        new ProfileStore(home).Save(new TenantProfile("work", "contoso.onmicrosoft.com", "00000000-0000-0000-0000-000000000010", "alice@contoso.com"));
        (GraphClient graph, _) = GraphFixtures.Client();
        return new BridgeDispatcher(new BridgeServices(
            new AuthService(home, _ => broker, SignInHints.Window),
            null,
            _ => graph,
            exception => InternalError.WriteToLog(exception, home))
        {
            OpenInBrowser = opened is null ? null : page =>
            {
                opened.Add(page);
                return Task.CompletedTask;
            },
        });
    }
}
