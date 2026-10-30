using System.CommandLine;
using System.Text;
using M365Aegis.Cli.Commands;
using M365Aegis.Cli.Output;
using M365Aegis.Core;
using M365Aegis.Core.Config;
using M365Aegis.Core.Diagnostics;
using M365Aegis.Core.Output;

namespace M365Aegis.Cli;

/// <summary>
/// The m365-aegis command line. The root carries the options every command honors: <c>--json</c> for the
/// envelope, <c>--table</c> for a human table, and neither picks by whether stdout is a terminal (D-006);
/// <c>--profile</c> names the tenant profile of <c>config.json</c> (task 002); <c>--verbose</c> writes MSAL's log,
/// without PII and scrubbed, into the home folder's <c>logs</c> folder (task 025).
/// </summary>
internal static class Program
{
    internal static async Task<int> Main(string[] args)
    {
        // The envelope and the table are UTF-8 on every console and in every pipe (D-006: accented names stay
        // readable), not the console's legacy code page, which a shell started from an older locale still has.
        Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        (RootCommand root, Option<bool> json, Option<bool> table, Option<bool> verbose) = Build();
        ParseResult parsed = root.Parse(args);
        if (parsed.Errors.Count > 0)
        {
            // A usage error is an answer too (D-006): the envelope in a pipe, the error line at a terminal.
            string message = string.Join(" ", parsed.Errors.Select(error => error.Message));
            var failure = new AegisFailure(FailureKind.Usage, message, $"see '{ProductInfo.Name} --help'");
            return CommandRunner.Fail(ResolveMode(parsed, json, table), failure);
        }

        if (parsed.GetValue(verbose))
        {
            // Before any command runs, so every MSAL client the command builds logs into the sink.
            LogSink.EnableVerbose(AegisHome.Resolve());
        }

        return await parsed.InvokeAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// The command tree and the root's options: what <see cref="Main"/> parses, and what the write-guard test walks to
    /// find every write command (task 071).
    /// </summary>
    internal static (RootCommand Root, Option<bool> Json, Option<bool> Table, Option<bool> Verbose) Build()
    {
        Option<bool> json = new("--json")
        {
            Description = "Print the JSON envelope (the default when stdout is not a terminal).",
            Recursive = true,
        };
        Option<bool> table = new("--table")
        {
            Description = "Print a table (the default when stdout is a terminal).",
            Recursive = true,
        };
        Option<string?> profile = new("--profile")
        {
            Description = "The tenant profile of config.json to use (the default one when omitted).",
            Recursive = true,
        };
        Option<bool> verbose = new("--verbose")
        {
            Description = "Write the sign-in library's log (no tokens, no personal data) into the logs folder of the home folder.",
            Recursive = true,
        };

        RootCommand root = new($"{ProductInfo.Name}: Microsoft 365 administration from the command line.");
        root.Options.Add(json);
        root.Options.Add(table);
        root.Options.Add(profile);
        root.Options.Add(verbose);
        root.Subcommands.Add(LoginCommand.Build(json, table, profile));
        root.Subcommands.Add(WhoAmICommand.Build(json, table, profile));
        root.Subcommands.Add(LogoutCommand.Build(json, table, profile));
        root.Subcommands.Add(DoctorCommand.Build(json, table, profile));
        root.Subcommands.Add(ProfileCommand.Build(json, table));
        root.Subcommands.Add(CallCommand.Build(json, table, profile));
        root.Subcommands.Add(GroupCommand.Build(json, table, profile));
        root.Subcommands.Add(TeamCommand.Build(json, table, profile));
        root.Subcommands.Add(SiteCommand.Build(json, table, profile));
        root.Subcommands.Add(TraceCommand.Build(json, table, profile));
        root.Subcommands.Add(SignInListCommand.Build(json, table, profile));
        root.Subcommands.Add(SecretsCommand.Build(json, table, profile));
        root.Subcommands.Add(AuditCommand.Build(json, table, profile));
        root.Subcommands.Add(MailboxCommand.Build(json, table, profile));
        root.Subcommands.Add(ProbeCommand.Build(json, table, profile));
        root.Subcommands.Add(PlanCommand.Build(json, table));
        return (root, json, table, verbose);
    }

    /// <summary>The output mode of a parse result: the explicit option wins, else the terminal decides.</summary>
    internal static OutputMode ResolveMode(ParseResult parsed, Option<bool> json, Option<bool> table)
    {
        if (parsed.GetValue(json))
        {
            return OutputMode.Json;
        }

        if (parsed.GetValue(table))
        {
            return OutputMode.Table;
        }

        return Console.IsOutputRedirected ? OutputMode.Json : OutputMode.Table;
    }
}
