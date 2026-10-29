using System.CommandLine;
using M365Aegis.Cli.Output;
using M365Aegis.Core.Auth;
using M365Aegis.Core.Output;

namespace M365Aegis.Cli.Commands;

/// <summary>
/// <c>m365-aegis whoami</c>: who is signed in and what the tokens carry, silently; exit code 3 with the login
/// command as the hint when there is no profile or no silent sign-in. The result type is <see cref="SignInInfo"/>,
/// the table <see cref="SignInTableView"/>.
/// </summary>
internal static class WhoAmICommand
{
    internal static Command Build(Option<bool> json, Option<bool> table, Option<string?> profile)
    {
        Command command = new("whoami", "Show the signed-in account and the state of the Graph and Exchange Online tokens.");
        command.SetAction((parsed, cancellationToken) => CommandRunner.RunAsync(
            Program.ResolveMode(parsed, json, table),
            Backend.Local,
            async () =>
            {
                SignInInfo info = await AuthService.Create().WhoAmIAsync(parsed.GetValue(profile), cancellationToken).ConfigureAwait(false);
                return CommandResult.Success(info);
            },
            SignInTableView.Instance));
        return command;
    }
}
