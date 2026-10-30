using System.CommandLine;
using System.Globalization;
using System.Text.Json;
using M365Aegis.Cli.Output;
using M365Aegis.Core.Graph;
using M365Aegis.Core.Operations;
using M365Aegis.Core.Output;

namespace M365Aegis.Cli.Commands;

/// <summary>
/// <c>m365-aegis call &lt;METHOD&gt; &lt;path&gt; [--beta] [--body &lt;json|@file&gt;] [--all] [--advanced] [--dry-run|--yes]</c>:
/// any Graph request, the answer unchanged as the envelope's data (task 003). A GET is sent as it is; any other method
/// changes the tenant and is a plan of one change (task 051): <c>--dry-run</c> prints it, no flag prints it with exit
/// code 7, and <c>--yes</c> sends it through the write guard, which records it (D-006). The result is the answer's
/// JSON, the table <see cref="CallTableView"/>, or the plan, the table <see cref="PlanTableView"/>.
/// </summary>
internal static class CallCommand
{
    internal static Command Build(Option<bool> json, Option<bool> table, Option<string?> profile)
    {
        Argument<string> method = new("method")
        {
            Description = "The HTTP method: GET, POST, PATCH, PUT or DELETE.",
        };
        Argument<string> path = new("path")
        {
            Description = "The path under the version (/me, /groups?$top=5), with /v1.0 or /beta in front, or a full https://graph.microsoft.com URL.",
        };
        Option<bool> beta = new("--beta")
        {
            Description = "Call the beta endpoint for a path without a version.",
        };
        Option<string?> body = new("--body")
        {
            Description = "The JSON body of a write, or @ and the path of a file that holds it.",
        };
        Option<bool> all = new("--all")
        {
            Description = "Follow @odata.nextLink and return every page's items in one answer.",
        };
        Option<bool> advanced = new("--advanced")
        {
            Description = "Send ConsistencyLevel: eventual, for an advanced query on directory objects ($search, $count, owners/$count); put $count=true in the path.",
        };
        // For the tests' authors (tests/fixtures/README.md): the fixture file the request maps to; nothing is sent.
        Option<bool> fixtureName = new("--fixture-name")
        {
            Description = "Print the fixture file the request is answered from under tests/fixtures/graph; nothing is sent.",
            Hidden = true,
        };
        Command command = new("call", "Send any Microsoft Graph request and print the answer unchanged.");
        command.Arguments.Add(method);
        command.Arguments.Add(path);
        command.Options.Add(beta);
        command.Options.Add(body);
        command.Options.Add(all);
        command.Options.Add(advanced);
        command.Options.Add(fixtureName);
        WriteFlags flags = WriteFlags.On(command, resume: false);
        command.SetAction((parsed, cancellationToken) =>
        {
            GraphCallOptions Options() => new(
                parsed.GetValue(method) ?? "",
                parsed.GetValue(path) ?? "",
                parsed.GetValue(beta),
                parsed.GetValue(body),
                parsed.GetValue(flags.Yes),
                parsed.GetValue(all),
                parsed.GetValue(advanced));

            if (parsed.GetValue(fixtureName))
            {
                return CommandRunner.RunAsync(
                    Program.ResolveMode(parsed, json, table),
                    Backend.Local,
                    () => Task.FromResult(GraphCall.FixtureName(Options())),
                    FixtureNameTableView.Instance);
            }

            OutputMode mode = Program.ResolveMode(parsed, json, table);
            bool read = string.Equals((parsed.GetValue(method) ?? "").Trim(), "GET", StringComparison.OrdinalIgnoreCase);
            if (read)
            {
                return parsed.GetValue(flags.DryRun)
                    ? Task.FromResult(CommandRunner.Fail(mode, new AegisFailure(FailureKind.Usage, "a GET changes nothing and has no plan", "drop --dry-run")))
                    : GraphRunner.RunAsync(parsed, json, table, profile, (graph, ct) => GraphCall.RunAsync(graph, Options(), ct), CallTableView.Instance, cancellationToken);
            }

            // A write is a plan of one change (task 051): --yes sends it through the guard and answers as a GET does.
            if (flags.Resolve(parsed, out AegisFailure? failure) is not { } writeMode)
            {
                return Task.FromResult(CommandRunner.Fail(mode, failure!));
            }

            PlanStore store = WriteRunner.Store(parsed, profile);
            if (writeMode == WriteMode.Apply)
            {
                return GraphRunner.RunAsync(parsed, json, table, profile, (graph, ct) => GraphCall.RunAsync(graph, Options(), store, ct), CallTableView.Instance, cancellationToken);
            }

            return GraphRunner.RunAsync(
                parsed, json, table, profile,
                (graph, ct) => PlanGuard.RunAsync(GraphCall.Plan(graph, Options()), writeMode, store, ct),
                PlanTableView.Instance,
                cancellationToken);
        });
        return command;
    }
}

/// <summary>The table of <c>call … --fixture-name</c>: the names of the URL's fixture, a field per row.</summary>
internal sealed class FixtureNameTableView : ITableView<FixtureName>
{
    /// <summary>The one instance.</summary>
    internal static FixtureNameTableView Instance { get; } = new();

    private FixtureNameTableView()
    {
    }

    public IReadOnlyList<string> Headers => ["field", "value"];

    public IEnumerable<IReadOnlyList<string>> Rows(FixtureName data) =>
    [
        ["url", data.Url],
        ["full name", data.FullName],
        ["stored name", data.StoredName],
        ["file", data.File],
        ["length", data.Length.ToString(CultureInfo.InvariantCulture)],
    ];

    public IEnumerable<string> Footer(FixtureName data)
    {
        yield return data.Fits
            ? "write the answer to the file; a .headers file and the .try-n forms go next to it"
            : string.Format(CultureInfo.InvariantCulture, "the file is longer than the {0} characters check-fixtures.py allows: shorten the request (a shorter id, fewer $select fields)", FixtureGraphTransport.MaxRepoPathLength);
    }
}

/// <summary>
/// The table of a raw answer: a collection (<c>value</c>) as one row per item with the first item's plain fields as
/// the columns, any other object as a field per row; nested values are written as compact JSON and long cells
/// are cut, so the envelope (<c>--json</c>) is where the whole answer is.
/// </summary>
internal sealed class CallTableView : ITableView<JsonElement?>
{
    private const int MaxColumns = 6;
    private const int MaxCell = 80;

    /// <summary>The one instance.</summary>
    internal static CallTableView Instance { get; } = new();

    private CallTableView()
    {
    }

    /// <summary>The headers when nothing is known of the data; <see cref="HeadersFor"/> picks the real ones.</summary>
    public IReadOnlyList<string> Headers => ["field", "value"];

    public IReadOnlyList<string> HeadersFor(JsonElement? answer) => answer is not { } data
        ? []
        : Items(data) is { } items ? Columns(items) : data.ValueKind == JsonValueKind.Object ? Headers : ["value"];

    public IEnumerable<IReadOnlyList<string>> Rows(JsonElement? answer)
    {
        if (answer is not { } data)
        {
            return [];
        }

        if (Items(data) is { } items)
        {
            string[] columns = Columns(items);
            return items.Select(item => (IReadOnlyList<string>)columns
                .Select(column => item.ValueKind == JsonValueKind.Object && item.TryGetProperty(column, out JsonElement value) ? Cell(value) : column == "value" ? Cell(item) : "")
                .ToArray());
        }

        if (data.ValueKind == JsonValueKind.Object)
        {
            return data.EnumerateObject()
                .Where(property => !property.Name.StartsWith("@odata.", StringComparison.Ordinal))
                .Select(property => (IReadOnlyList<string>)[property.Name, Cell(property.Value)]);
        }

        return [[data.ValueKind == JsonValueKind.Null ? "(no content)" : Cell(data)]];
    }

    public IEnumerable<string> Footer(JsonElement? answer)
    {
        if (answer is not { } data)
        {
            yield break;
        }

        if (Items(data) is { } items)
        {
            int fields = items.Count == 0 ? 0 : PlainFields(items[0]).Count();
            yield return string.Format(CultureInfo.InvariantCulture, "{0} item(s){1}", items.Count, fields > MaxColumns ? $"; the first {MaxColumns} of {fields} fields, --json for all" : "");
        }

        if (data.ValueKind == JsonValueKind.Object && data.TryGetProperty("@odata.nextLink", out _))
        {
            yield return "more pages follow: --all fetches them";
        }
    }

    /// <summary>The items of a collection answer, or null.</summary>
    internal static IReadOnlyList<JsonElement>? Items(JsonElement data)
    {
        if (data.ValueKind == JsonValueKind.Array)
        {
            return data.EnumerateArray().ToList();
        }

        return data.ValueKind == JsonValueKind.Object && data.TryGetProperty("value", out JsonElement value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().ToList()
            : null;
    }

    private static string[] Columns(IReadOnlyList<JsonElement> items) =>
        items.Count == 0 ? ["value"] : PlainFields(items[0]).Take(MaxColumns).ToArray() is { Length: > 0 } names ? names : ["value"];

    private static IEnumerable<string> PlainFields(JsonElement item) => item.ValueKind != JsonValueKind.Object
        ? []
        : item.EnumerateObject()
            .Where(property => !property.Name.StartsWith("@odata.", StringComparison.Ordinal)
                && property.Value.ValueKind is not JsonValueKind.Object and not JsonValueKind.Array)
            .Select(property => property.Name);

    private static string Cell(JsonElement value)
    {
        string text = value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? "",
            JsonValueKind.Null => "-",
            _ => value.GetRawText(),
        };
        text = text.ReplaceLineEndings(" ");
        return text.Length > MaxCell ? string.Concat(text.AsSpan(0, MaxCell - 1), "…") : text;
    }
}
