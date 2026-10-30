using M365Aegis.Core.Auth;
using M365Aegis.Core.Config;
using M365Aegis.Core.Output;
using M365Aegis.Core.Tests.Config;

namespace M365Aegis.Core.Tests.Auth;

/// <summary>
/// The hints of a failed sign-in per surface (task 046): every failure the sign-in and the profiles can end in
/// carries the command line's hint, word for word as before, or the window's, which names the sign-in card's buttons
/// or the administrator and never a command; the failure's kind and sentence are the same on both.
/// </summary>
public sealed class SignInHintsTests
{
    private static readonly TenantProfile Profile = new("work", "contoso.onmicrosoft.com", "00000000-0000-0000-0000-000000000010", "alice@contoso.com");

    /// <summary>Every failure, with the command line's hint as it was before the window got its own.</summary>
    public static TheoryData<string, string> Failures => new()
    {
        { "silent", "run `m365-aegis login`" },
        { "picker-cancelled", "run `m365-aegis login` again" },
        { "picker-failed", "run `m365-aegis login --device-code` to sign in through a browser instead" },
        { "device-code-failed", "run `m365-aegis login --device-code` again and finish the browser step before the code expires" },
        { "no-profile", "run `m365-aegis login --tenant <tenant id or domain> --client-id <application id> --account <user@domain>` once" },
        { "unknown-profile", "run `m365-aegis login --tenant <tenant id or domain> --client-id <application id> --account <user@domain>` once" },
        { "no-default", "name one with --profile, or set \"default\" in the file" },
        { "broken-file", "fix the file, or delete it and run `m365-aegis login` again" },
        { "incomplete-profile", "fix the file, or remove the profile with `m365-aegis profile remove`" },
    };

    [Theory]
    [MemberData(nameof(Failures))]
    public async Task EachFailureHasTheCommandLinesHintUnchangedAndTheWindowsOwn(string failure, string commandLineHint)
    {
        AegisFailure onTheCommandLine = await FailAsync(failure, SignInHints.CommandLine);
        AegisFailure inTheWindow = await FailAsync(failure, SignInHints.Window);

        Assert.Equal(commandLineHint, onTheCommandLine.Hint);
        Assert.Equal(WindowHint(failure), inTheWindow.Hint);
        Assert.Equal(FailureKind.Auth, inTheWindow.Kind);
        Assert.Equal(onTheCommandLine.Kind, inTheWindow.Kind);
        Assert.Equal(Normalized(onTheCommandLine.Message), Normalized(inTheWindow.Message));
    }

    [Fact]
    public void TheWindowsHintsNameNoCommand()
    {
        foreach (string hint in Hints(SignInHints.Window))
        {
            Assert.DoesNotContain("m365-aegis", hint, StringComparison.Ordinal);
            Assert.DoesNotContain("`", hint, StringComparison.Ordinal);
            Assert.DoesNotContain("--", hint, StringComparison.Ordinal);
        }

        Assert.Equal(Hints(SignInHints.Window).Count, Hints(SignInHints.Window).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task TheWindowsServiceGivesItsHintsToEverySilentTokenToo()
    {
        string home = ProfileStoreTests.TempHome();
        var service = new AuthService(home, _ => new FakeTokenBroker(), SignInHints.Window);
        service.Profiles.Save(Profile);

        AegisException whoami = await Assert.ThrowsAsync<AegisException>(() => service.WhoAmIAsync(null, CancellationToken.None));
        AegisException token = await Assert.ThrowsAsync<AegisException>(() => service.GetTokenAsync(Resource.Exchange, null, CancellationToken.None));

        Assert.Equal(SignInHints.Window.SignInAgain, whoami.Failure.Hint);
        Assert.Equal(SignInHints.Window.SignInAgain, token.Failure.Hint);
        Assert.Same(SignInHints.Window, service.Hints);
        Assert.Same(SignInHints.CommandLine, new AuthService(home, _ => new FakeTokenBroker()).Hints);
    }

    private static string WindowHint(string failure) => failure switch
    {
        "silent" => SignInHints.Window.SignInAgain,
        "picker-cancelled" => SignInHints.Window.PickerCancelled,
        "picker-failed" => SignInHints.Window.PickerFailed,
        "device-code-failed" => SignInHints.Window.DeviceCodeFailed,
        "no-profile" or "unknown-profile" => SignInHints.Window.NoProfile,
        "no-default" => SignInHints.Window.NoDefaultProfile,
        "broken-file" => SignInHints.Window.BrokenProfileFile,
        "incomplete-profile" => SignInHints.Window.IncompleteProfile,
        _ => throw new ArgumentOutOfRangeException(nameof(failure), failure, null),
    };

    /// <summary>The failure a scenario ends in, on the given surface.</summary>
    private static async Task<AegisFailure> FailAsync(string failure, SignInHints hints)
    {
        AegisException exception = failure switch
        {
            "silent" => await FlowAsync(new FakeTokenBroker(), SignInMode.SilentOnly, hints),
            "picker-cancelled" => await FlowAsync(
                new FakeTokenBroker { InteractiveFailure = new BrokerException(BrokerErrorKind.Cancelled, "authentication_canceled", "closed") },
                SignInMode.Interactive,
                hints),
            "picker-failed" => await FlowAsync(new FakeTokenBroker(), SignInMode.Interactive, hints),
            "device-code-failed" => await FlowAsync(new FakeTokenBroker(), SignInMode.DeviceCode, hints),
            _ => ProfileFailure(failure, hints),
        };
        return exception.Failure;
    }

    private static Task<AegisException> FlowAsync(FakeTokenBroker broker, SignInMode mode, SignInHints hints) =>
        Assert.ThrowsAsync<AegisException>(() => SignInFlow.RunAsync(broker, Profile, Resource.Graph, mode, _ => Task.CompletedTask, hints, CancellationToken.None));

    /// <summary>A profile failure in a home folder of its own; the file's path is the folder's, which the message names.</summary>
    private static AegisException ProfileFailure(string failure, SignInHints hints)
    {
        string home = ProfileStoreTests.TempHome();
        var store = new ProfileStore(home, hints);
        string? name = null;
        switch (failure)
        {
            case "unknown-profile":
                store.Save(Profile);
                name = "other";
                break;
            case "no-default":
                Directory.CreateDirectory(home);
                File.WriteAllText(AegisHome.ConfigPath(home), """{"profiles":{"a":{"tenant":"t","clientId":"c","account":"a@contoso.com"},"b":{"tenant":"t","clientId":"c","account":"b@contoso.com"}}}""");
                break;
            case "broken-file":
                Directory.CreateDirectory(home);
                File.WriteAllText(AegisHome.ConfigPath(home), "{ not json");
                break;
            case "incomplete-profile":
                Directory.CreateDirectory(home);
                File.WriteAllText(AegisHome.ConfigPath(home), """{"default":"a","profiles":{"a":{"tenant":"t","account":"a@contoso.com"}}}""");
                break;
        }

        return Assert.Throws<AegisException>(() => store.Resolve(name));
    }

    /// <summary>A message with its home folder's path left out: each scenario runs in a folder of its own.</summary>
    private static string Normalized(string message) =>
        System.Text.RegularExpressions.Regex.Replace(message, @"\S*m365-aegis-tests\S*", "<home>");

    private static List<string> Hints(SignInHints hints) =>
        typeof(SignInHints).GetProperties()
            .Where(property => property.PropertyType == typeof(string))
            .Select(property => (string)property.GetValue(hints)!)
            .ToList();
}
