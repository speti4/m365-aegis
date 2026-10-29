using System.CommandLine;
using System.Globalization;
using M365Aegis.Cli.Output;
using M365Aegis.Core.Auth;
using M365Aegis.Core.Exchange;
using M365Aegis.Core.Exchange.Sidecar;
using M365Aegis.Core.Output;

namespace M365Aegis.Cli.Commands;

/// <summary>
/// <c>m365-aegis probe exchange-connect|admin-api</c>, hidden from the help: the live probes of tasks 009 and 010,
/// run once by the maintainer against the tenant. They print timings, permissions, statuses and the shape of an
/// answer; never a token, a tenant identifier or a value the service returned.
/// </summary>
internal static class ProbeCommand
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };

    internal static Command Build(Option<bool> json, Option<bool> table, Option<string?> profile)
    {
        Command probe = new("probe", "Live probes of the Exchange Online sign-in (maintainer only).") { Hidden = true };

        Option<string?> connectWith = new("--connect-with")
        {
            Description = "What Connect-ExchangeOnline gets besides the token: organization (the default), upn or both.",
        };
        Command connect = new("exchange-connect", "Connect the sidecar with the broker's token, run a cmdlet, reconnect, run it again; print the timings.");
        connect.Options.Add(connectWith);
        connect.SetAction((parsed, cancellationToken) =>
        {
            ExchangeConnectWith with = ExchangeConnection.ParseConnectWith(parsed.GetValue(connectWith));
            return CommandRunner.RunAsync(
                Program.ResolveMode(parsed, json, table),
                Backend.Pwsh,
                async requestLog =>
                {
                    await using ExchangeSession session = ExchangeConnection.Create(parsed.GetValue(profile), requestLog, with);
                    return CommandResult.Success(await ExchangeProbes.ConnectAsync(session, with, TimeProvider.System, cancellationToken).ConfigureAwait(false));
                },
                ConnectProbeTableView.Instance);
        });

        Option<string?> probeMailbox = new("--mailbox") { Description = "Also read this mailbox's Send on Behalf delegates by name and its folder's permissions (task 042)." };
        Option<string?> probeFolder = new("--folder") { Description = "The folder of --mailbox, as the mailbox's language names it (Calendar by default)." };
        Option<string?> probeGroup = new("--group") { Description = "Also read the first members of this distribution group (task 042)." };
        Command adminApi = new("admin-api", "Call the Exchange Online Admin API v2.0 (OrganizationConfig, one page of Mailbox) with the broker's token; print the statuses and the shapes.");
        adminApi.Options.Add(probeMailbox);
        adminApi.Options.Add(probeFolder);
        adminApi.Options.Add(probeGroup);
        adminApi.SetAction((parsed, cancellationToken) => CommandRunner.RunAsync(
            Program.ResolveMode(parsed, json, table),
            Backend.AdminApi,
            async () =>
            {
                TokenResult token = await AuthService.Create().GetTokenAsync(Resource.Exchange, parsed.GetValue(profile), cancellationToken).ConfigureAwait(false);
                var targets = new AdminApiProbeTargets(parsed.GetValue(probeMailbox), parsed.GetValue(probeFolder), parsed.GetValue(probeGroup));
                AdminApiProbeReport report = await ExchangeProbes.AdminApiAsync(Http, ExchangeConnection.ToExchangeToken(token), targets, cancellationToken).ConfigureAwait(false);
                return CommandResult.Success(report);
            },
            AdminApiProbeTableView.Instance));

        Option<int> runs = new("--runs") { Description = "Fresh pwsh processes per variant (1 to 20).", DefaultValueFactory = _ => 5 };
        Option<string?> variants = new("--variants")
        {
            Description = "The connect variants, comma-separated: default, skip-format-data, command-name (all three when omitted).",
        };
        Command measure = new("exchange-measure", "Time the cold connect of fresh sidecars per variant with one broker token (task 011); no interactive sign-in.");
        measure.Options.Add(runs);
        measure.Options.Add(variants);
        measure.SetAction((parsed, cancellationToken) => CommandRunner.RunAsync(
            Program.ResolveMode(parsed, json, table),
            Backend.Pwsh,
            async requestLog =>
            {
                IReadOnlyList<ConnectVariant> chosen = string.IsNullOrWhiteSpace(parsed.GetValue(variants))
                    ? [ConnectVariant.Default, ConnectVariant.SkipFormatData, ConnectVariant.CommandName]
                    : parsed.GetValue(variants)!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(ConnectMeasurement.ParseVariant).Distinct().ToList();
                ExchangeSessionOptions session = ExchangeConnection.Options(parsed.GetValue(profile), requestLog);
                return CommandResult.Success(await ConnectMeasurement.RunAsync(session, chosen, parsed.GetValue(runs), cancellationToken).ConfigureAwait(false));
            },
            ConnectMeasurementTableView.Instance));

        probe.Subcommands.Add(connect);
        probe.Subcommands.Add(measure);
        probe.Subcommands.Add(adminApi);
        return probe;
    }
}

/// <summary>The table of <c>probe exchange-connect</c>: a measurement per row.</summary>
internal sealed class ConnectProbeTableView : ITableView<ConnectProbeReport>
{
    internal static ConnectProbeTableView Instance { get; } = new();

    private ConnectProbeTableView()
    {
    }

    public IReadOnlyList<string> Headers => ["measure", "value"];

    public IEnumerable<IReadOnlyList<string>> Rows(ConnectProbeReport data) =>
    [
        ["connect with", data.ConnectWith],
        ["pwsh", data.Pwsh],
        ["module", data.Module ?? "-"],
        ["start to hello", Ms(data.StartMs)],
        ["module import", Ms(data.ImportMs)],
        ["Connect-ExchangeOnline", Ms(data.ConnectMs)],
        ["first cmdlet", Ms(data.FirstCmdletMs)],
        ["reconnect (fresh token)", Ms(data.ReconnectMs)],
        ["cmdlet after reconnect", Ms(data.AfterReconnectMs)],
        ["token minutes left", data.TokenMinutesLeft.ToString("0.0", CultureInfo.InvariantCulture)],
        ["token permissions", string.Join(" ", data.TokenPermissions)],
        ["Manage and ManageV2 on one token", data.CarriesManageAndManageV2 ? "yes" : "no"],
    ];

    private static string Ms(long milliseconds) => string.Create(CultureInfo.InvariantCulture, $"{milliseconds} ms");
}

/// <summary>The table of <c>probe exchange-measure</c>: a row per run, the environment and the medians under the table.</summary>
internal sealed class ConnectMeasurementTableView : ITableView<ConnectMeasurementReport>
{
    internal static ConnectMeasurementTableView Instance { get; } = new();

    private ConnectMeasurementTableView()
    {
    }

    public IReadOnlyList<string> Headers => ["variant", "run", "total ms", "start ms", "import ms", "connect ms", "first call ms", "error"];

    public IEnumerable<IReadOnlyList<string>> Rows(ConnectMeasurementReport data) => data.Runs.Select(run => (IReadOnlyList<string>)
    [
        ConnectMeasurement.Name(run.Variant),
        run.Run.ToString(CultureInfo.InvariantCulture),
        run.TotalMs.ToString(CultureInfo.InvariantCulture),
        run.StartMs.ToString(CultureInfo.InvariantCulture),
        run.ImportMs.ToString(CultureInfo.InvariantCulture),
        run.ConnectMs.ToString(CultureInfo.InvariantCulture),
        run.FirstCallMs.ToString(CultureInfo.InvariantCulture),
        run.Error ?? (run.Missing.Count > 0 ? "does not answer " + string.Join(", ", run.Missing) : "-"),
    ]);

    public IEnumerable<string> Footer(ConnectMeasurementReport data)
    {
        MeasureEnvironment environment = data.Environment;
        yield return $"pwsh {environment.Pwsh ?? "?"}, ExchangeOnlineManagement {environment.Module ?? "?"}; token permissions: {string.Join(" ", environment.TokenPermissions)}";
        yield return string.Create(
            CultureInfo.InvariantCulture,
            $"under OneDrive: the module {Flag(environment.ModuleUnderOneDrive)}, the first PSModulePath entry {Flag(environment.FirstModulePathUnderOneDrive)}, PSModulePath entries {environment.ModulePathEntriesUnderOneDrive}, Documents {Flag(environment.DocumentsUnderOneDrive)}");
        foreach (VariantSummary summary in data.Summary)
        {
            yield return string.Create(
                CultureInfo.InvariantCulture,
                $"{ConnectMeasurement.Name(summary.Variant)}: {summary.Runs - summary.Failed} of {summary.Runs} run(s); median (spread) total {Ms(summary.Total)}, start {Ms(summary.Start)}, import {Ms(summary.Import)}, connect {Ms(summary.Connect)}, first call {Ms(summary.FirstCall)}; answers the allow-list: {(summary.AnswersAllowList ? "yes" : "no")}");
        }
    }

    private static string Flag(bool? value) => value switch { true => "yes", false => "no", null => "?" };

    private static string Ms(Spread spread) =>
        spread.Median is { } median ? string.Create(CultureInfo.InvariantCulture, $"{median} ({spread.Min}-{spread.Max})") : "-";
}

/// <summary>The table of <c>probe admin-api</c>: a row per call, the verdict under the table.</summary>
internal sealed class AdminApiProbeTableView : ITableView<AdminApiProbeReport>
{
    internal static AdminApiProbeTableView Instance { get; } = new();

    private AdminApiProbeTableView()
    {
    }

    public IReadOnlyList<string> Headers => ["endpoint", "anchor", "status", "ms", "items", "next link", "error"];

    public IEnumerable<IReadOnlyList<string>> Rows(AdminApiProbeReport data) => data.Calls.Select(call => (IReadOnlyList<string>)
    [
        call.Endpoint,
        call.AnchorForm,
        call.Status?.ToString(CultureInfo.InvariantCulture) ?? "-",
        call.ElapsedMs.ToString(CultureInfo.InvariantCulture),
        call.Items?.ToString(CultureInfo.InvariantCulture) ?? "-",
        call.NextLinkParameters is null ? "-" : string.Join(",", call.NextLinkParameters),
        call.ErrorCode is null && call.ErrorMessage is null ? "-" : $"{call.ErrorCode} {call.ErrorMessage}".Trim(),
    ]);

    public IEnumerable<string> Footer(AdminApiProbeReport data)
    {
        yield return data.Answered ? "the Admin API answered OrganizationConfig with the broker's token" : "the Admin API did not answer OrganizationConfig";
        yield return "token permissions: " + string.Join(" ", data.TokenPermissions);
        foreach (AdminApiCall call in data.Calls)
        {
            yield return $"{call.Endpoint} ({call.AnchorForm}) shape: {string.Join("; ", call.Shape)}";
        }
    }
}
