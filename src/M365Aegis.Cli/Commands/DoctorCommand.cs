using System.CommandLine;
using System.Diagnostics;
using System.Globalization;
using M365Aegis.Cli.Output;
using M365Aegis.Core.Diagnostics;
using M365Aegis.Core.Output;

namespace M365Aegis.Cli.Commands;

/// <summary>
/// <c>m365-aegis doctor</c>: the prerequisites, one line each, and exit code 10 when a required one is missing.
/// <c>--offline</c> skips the checks that need the network (the sign-in and the endpoints).
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
        command.SetAction(parsed => Run(Program.ResolveMode(parsed, json, table)));
        return command;
    }

    private static int Run(OutputMode mode)
    {
        var stopwatch = Stopwatch.StartNew();
        // The online checks arrive with task 008; until then every run is the offline one.
        DoctorReport report = Doctor.RunOffline();
        stopwatch.Stop();

        if (mode == OutputMode.Json)
        {
            var meta = new EnvelopeMeta([], stopwatch.ElapsedMilliseconds, "local");
            Envelope<DoctorReport> envelope = report.Healthy
                ? Envelope.Success(report, meta)
                : Envelope.Failure(new EnvelopeError("doctor-failed", "a required check failed", "see data"), meta, report);
            Console.Out.WriteLine(EnvelopeJson.Serialize(envelope));
        }
        else
        {
            string[] headers = ["check", "status", "detail"];
            List<IReadOnlyList<string>> rows = report.Checks
                .Select(check => (IReadOnlyList<string>)[check.Name, Status(check), check.Detail])
                .ToList();
            Console.Out.Write(TableWriter.Render(headers, rows));
            Console.Out.WriteLine();
            Console.Out.WriteLine(string.Format(CultureInfo.InvariantCulture, "home: {0}", report.Home));
            Console.Out.WriteLine(report.Healthy ? "ok" : "a required check failed");
        }

        return (int)(report.Healthy ? ExitCode.Ok : ExitCode.DoctorFailed);
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
