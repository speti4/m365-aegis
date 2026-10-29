using System.CommandLine;
using System.Globalization;
using M365Aegis.Cli.Output;
using M365Aegis.Core.Auth;
using M365Aegis.Core.Config;
using M365Aegis.Core.Output;

namespace M365Aegis.Cli.Commands;

/// <summary>
/// <c>m365-aegis profile list | use &lt;name&gt; | remove &lt;name&gt;</c> over the tenant profiles of <c>config.json</c>
/// (task 024): <c>list</c> marks the default, <c>use</c> moves it, <c>remove</c> signs the profile's account out and
/// deletes the profile. A profile is still written by <c>login</c>'s flags. The result types are
/// <see cref="ProfileList"/> (<see cref="ProfileListTableView"/>) and <see cref="ProfileRemoval"/>
/// (<see cref="ProfileRemovalTableView"/>).
/// </summary>
internal static class ProfileCommand
{
    internal static Command Build(Option<bool> json, Option<bool> table)
    {
        Command list = new("list", "List the tenant profiles; the default one is marked.");
        list.SetAction(parsed => CommandRunner.Run(
            Program.ResolveMode(parsed, json, table),
            Backend.Local,
            () => CommandResult.Success(new ProfileStore(AegisHome.Resolve()).Describe()),
            ProfileListTableView.Instance));

        Argument<string> useName = new("name") { Description = "The profile that commands without --profile use from now on." };
        Command use = new("use", "Make a profile the default one.");
        use.Arguments.Add(useName);
        use.SetAction(parsed => CommandRunner.Run(
            Program.ResolveMode(parsed, json, table),
            Backend.Local,
            () => CommandResult.Success(new ProfileStore(AegisHome.Resolve()).Use(parsed.GetRequiredValue(useName))),
            ProfileListTableView.Instance));

        Argument<string> removeName = new("name") { Description = "The profile to remove." };
        Command remove = new("remove", "Sign a profile's account out and delete the profile.");
        remove.Arguments.Add(removeName);
        remove.SetAction((parsed, cancellationToken) => CommandRunner.RunAsync(
            Program.ResolveMode(parsed, json, table),
            Backend.Local,
            async () =>
            {
                ProfileRemoval removal = await AuthService.Create().RemoveProfileAsync(parsed.GetRequiredValue(removeName), cancellationToken).ConfigureAwait(false);
                return CommandResult.Success(removal);
            },
            ProfileRemovalTableView.Instance));

        Command command = new("profile", "List the tenant profiles of config.json, pick the default one, remove one.");
        command.Subcommands.Add(list);
        command.Subcommands.Add(use);
        command.Subcommands.Add(remove);
        return command;
    }

    /// <summary>The table of <c>profile list</c> and <c>profile use</c>: a row per profile, the default marked, then the file.</summary>
    internal sealed class ProfileListTableView : ITableView<ProfileList>
    {
        /// <summary>The one instance.</summary>
        internal static ProfileListTableView Instance { get; } = new();

        private ProfileListTableView()
        {
        }

        public IReadOnlyList<string> Headers => ["default", "name", "tenant", "account", "client id"];

        public IEnumerable<IReadOnlyList<string>> Rows(ProfileList data) =>
            data.Profiles.Select(profile => (IReadOnlyList<string>)[profile.IsDefault ? "*" : "", profile.Name, profile.Tenant, profile.Account, profile.ClientId]);

        public IEnumerable<string> Footer(ProfileList data)
        {
            yield return string.Format(CultureInfo.InvariantCulture, "file: {0}", data.File);
            if (data.Profiles.Count == 0)
            {
                yield return $"no profile yet: {ProfileStore.LoginHint}";
            }
            else if (data.Default is null)
            {
                yield return "no default: commands need --profile, or `m365-aegis profile use <name>` picks one";
            }
        }
    }

    /// <summary>The table of <c>profile remove</c>.</summary>
    internal sealed class ProfileRemovalTableView : ITableView<ProfileRemoval>
    {
        /// <summary>The one instance.</summary>
        internal static ProfileRemovalTableView Instance { get; } = new();

        private ProfileRemovalTableView()
        {
        }

        public IReadOnlyList<string> Headers => ["field", "value"];

        public IEnumerable<IReadOnlyList<string>> Rows(ProfileRemoval data)
        {
            yield return ["profile", data.Profile];
            yield return ["account", data.Account];
            yield return ["signed out", data.SignedOut
                ? string.Format(CultureInfo.InvariantCulture, "yes, {0} account record(s) removed", data.RemovedAccounts)
                : "no: another profile uses the same app registration and account"];
            yield return ["default", data.Default ?? "-"];
        }

        public IEnumerable<string> Footer(ProfileRemoval data)
        {
            yield return "the profile is removed";
        }
    }
}
