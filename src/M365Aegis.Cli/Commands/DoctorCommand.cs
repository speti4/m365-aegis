using System.CommandLine;
using System.Globalization;
using M365Aegis.Cli.Output;
using M365Aegis.Core.Diagnostics;
using M365Aegis.Core.Output;

namespace M365Aegis.Cli.Commands;

/// <summary>
/// <c>m365-aegis doctor</c>: the prerequisites, one line each, and exit code 10 when a required one is missing.
/// <c>--offline</c> skips the checks that need the network (the sign-in and the endpoints). The result type is
/// <see cref="DoctorReport"/>; the envelope carries it as it is, the table is <see cref="DoctorTableView"/>.
/// </summary>
internal static class DoctorCommand
{
    internal static Command Build(Option<bool> json, Option<bool> table)
    {
        Option<bool> offline = new("--offline")
        {
            Description = "Skip the checks that need the network.",
        };
        Command command = new("doctor", "Check the prerequisites: pwsh, the Exchange Online module, WebView2, the sign-in.");
        command.Options.Add(offline);
        command.SetAction(parsed => CommandRunner.Run(
            Program.ResolveMode(parsed, json, table),
            Backend.Local,
            // The online checks arrive with task 008; until then every run is the offline one.
            () => Doctor.RunOffline().ToResult(),
            DoctorTableView.Instance));
        return command;
    }

    /// <summary>The table of a doctor report: a row per check, then the home folder and the verdict.</summary>
    internal sealed class DoctorTableView : ITableView<DoctorReport>
    {
        /// <summary>The one instance.</summary>
        internal static DoctorTableView Instance { get; } = new();

        private DoctorTableView()
        {
        }

        public IReadOnlyList<string> Headers => ["check", "status", "detail"];

        public IEnumerable<IReadOnlyList<string>> Rows(DoctorReport data) =>
            data.Checks.Select(check => (IReadOnlyList<string>)[check.Name, Status(check), check.Detail]);

        public IEnumerable<string> Footer(DoctorReport data)
        {
            yield return string.Format(CultureInfo.InvariantCulture, "home: {0}", data.Home);
            if (data.Healthy)
            {
                yield return "ok";
            }
        }

        private static string Status(DoctorCheck check) => check.Status switch
        {
            CheckStatus.Pass => "pass",
            CheckStatus.Warn => "warn",
            CheckStatus.Fail => check.Required ? "FAIL" : "fail",
            CheckStatus.Skipped => "skip",
            _ => "?",
        };
    }
}
