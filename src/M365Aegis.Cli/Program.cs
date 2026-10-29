using System.CommandLine;
using M365Aegis.Cli.Commands;
using M365Aegis.Cli.Output;
using M365Aegis.Core;
using M365Aegis.Core.Output;

namespace M365Aegis.Cli;

/// <summary>
/// The m365-aegis command line. The root carries the output options every command honors (D-006): <c>--json</c>
/// for the envelope, <c>--table</c> for a human table, and neither picks by whether stdout is a terminal.
/// </summary>
internal static class Program
{
    internal static int Main(string[] args)
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

        RootCommand root = new($"{ProductInfo.Name}: Microsoft 365 administration from the command line.");
        root.Options.Add(json);
        root.Options.Add(table);
        root.Subcommands.Add(DoctorCommand.Build(json, table));

        ParseResult parsed = root.Parse(args);
        if (parsed.Errors.Count > 0)
        {
            // A usage error is an answer too (D-006): the envelope in a pipe, the error line at a terminal.
            string message = string.Join(" ", parsed.Errors.Select(error => error.Message));
            var failure = new AegisFailure(FailureKind.Usage, message, $"see '{ProductInfo.Name} --help'");
            return CommandRunner.Fail(ResolveMode(parsed, json, table), failure);
        }

        return parsed.Invoke();
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
