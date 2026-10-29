using System.CommandLine;
using M365Aegis.Cli.Output;
using M365Aegis.Core.Auth;
using M365Aegis.Core.Output;

namespace M365Aegis.Cli.Commands;

/// <summary>
/// <c>m365-aegis logout</c>: forgets the profile's account in the cache and in the Windows account manager's record
/// for this app; the profile stays in <c>config.json</c>. The result type is <see cref="SignOutInfo"/>, the table
/// <see cref="SignOutTableView"/>.
/// </summary>
internal static class LogoutCommand
{
    internal static Command Build(Option<bool> json, Option<bool> table, Option<string?> profile)
    {
        Command command = new("logout", "Sign the profile's account out; the profile stays.");
        command.SetAction((parsed, cancellationToken) => CommandRunner.RunAsync(
            Program.ResolveMode(parsed, json, table),
            Backend.Local,
            async () =>
            {
                SignOutInfo info = await AuthService.Create().LogoutAsync(parsed.GetValue(profile), cancellationToken).ConfigureAwait(false);
                return CommandResult.Success(info);
            },
            SignOutTableView.Instance));
        return command;
    }
}
