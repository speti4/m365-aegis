using System.CommandLine;
using System.Globalization;
using M365Aegis.Cli.Output;
using M365Aegis.Core.Config;
using M365Aegis.Core.Diagnostics;
using M365Aegis.Core.Output;

namespace M365Aegis.Cli.Commands;

/// <summary>
/// <c>m365-aegis doctor</c>: the prerequisites, one line each, a fix line for each warning and failure, and exit code
/// 10 when a required one is missing (task 008). <c>--offline</c> skips the checks that need the network (the sign-in
/// and the endpoints). The result type is <see cref="DoctorReport"/>; the envelope carries it as it is, the table is
/// <see cref="DoctorTableView"/>.
/// </summary>
internal static class DoctorCommand
{
    internal static Command Build(Option<bool> json, Option<bool> table, Option<string?> profile)
    {
        Option<bool> offline = new("--offline")
        {
            Description = "Skip the checks that need the network (the sign-in and the endpoints).",
        };
        Command command = new("doctor", "Check the prerequisites: pwsh and the Exchange Online module, the sidecar script, WebView2, the device, the sign-in, the endpoints.");
        command.Options.Add(offline);
        command.SetAction((parsed, cancellationToken) => CommandRunner.RunAsync(
            Program.ResolveMode(parsed, json, table),
            Backend.Local,
            async () =>
            {
                DoctorReport report = await Doctor.RunAsync(AegisHome.Resolve(), parsed.GetValue(profile), parsed.GetValue(offline), cancellationToken).ConfigureAwait(false);
                return report.ToResult();
            },
            DoctorTableView.Instance));
        return command;
    }

    /// <summary>The table of a doctor report: a row per check, then the home folder, a fix line per warning and failure, and the verdict.</summary>
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
            foreach (DoctorCheck check in data.Checks.Where(check => check.Hint is not null && check.Status is CheckStatus.Warn or CheckStatus.Fail))
            {
                yield return $"fix {check.Name}: {check.Hint}";
            }

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
