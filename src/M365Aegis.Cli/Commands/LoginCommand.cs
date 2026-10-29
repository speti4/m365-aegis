using System.CommandLine;
using M365Aegis.Cli.Output;
using M365Aegis.Core.Auth;
using M365Aegis.Core.Output;

namespace M365Aegis.Cli.Commands;

/// <summary>
/// <c>m365-aegis login</c>: signs in through the Windows account manager (silent, then the picker), or with a
/// device code where there is no window or under <c>--device-code</c>; with <c>--tenant</c>, <c>--client-id</c>
/// and <c>--account</c> it writes the tenant profile first. The result type is <see cref="SignInInfo"/>, the
/// table <see cref="SignInTableView"/>; the device-code sentence goes to stderr.
/// </summary>
internal static class LoginCommand
{
    internal static Command Build(Option<bool> json, Option<bool> table, Option<string?> profile)
    {
        Option<string?> tenant = new("--tenant")
        {
            Description = "The tenant id or a verified domain (contoso.onmicrosoft.com); with --client-id and --account, writes the profile.",
        };
        Option<string?> clientId = new("--client-id")
        {
            Description = "The application (client) id of the public-client app registration.",
        };
        Option<string?> account = new("--account")
        {
            Description = "The user principal name to sign in as.",
        };
        Option<bool> deviceCode = new("--device-code")
        {
            Description = "Sign in through a browser with a device code instead of the Windows account manager.",
        };
        Command command = new("login", "Sign in through the Windows account manager and save the tenant profile.");
        command.Options.Add(tenant);
        command.Options.Add(clientId);
        command.Options.Add(account);
        command.Options.Add(deviceCode);
        command.SetAction((parsed, cancellationToken) => CommandRunner.RunAsync(
            Program.ResolveMode(parsed, json, table),
            Backend.Local,
            async () =>
            {
                var options = new LoginOptions(
                    parsed.GetValue(profile),
                    parsed.GetValue(tenant),
                    parsed.GetValue(clientId),
                    parsed.GetValue(account),
                    parsed.GetValue(deviceCode));
                SignInInfo info = await AuthService.Create().LoginAsync(options, DeviceCodePrompts.OnStderr, cancellationToken).ConfigureAwait(false);
                return CommandResult.Success(info);
            },
            SignInTableView.Instance));
        return command;
    }
}
