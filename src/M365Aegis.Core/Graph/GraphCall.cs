using System.Text.Json;
using System.Text.Json.Nodes;
using M365Aegis.Core.Operations;
using M365Aegis.Core.Output;

namespace M365Aegis.Core.Graph;

/// <summary>What <c>m365-aegis call</c> was asked for.</summary>
/// <param name="Method">The HTTP method, any case: GET, POST, PATCH, PUT or DELETE.</param>
/// <param name="Path">
/// The path under the version (<c>/me</c>), the same with the version in front (<c>/v1.0/me</c>,
/// <c>/beta/me</c>), or an absolute <c>https://graph.microsoft.com/…</c> URL.
/// </param>
/// <param name="Beta">Use the beta endpoint for a path without a version.</param>
/// <param name="Body">The JSON body, or <c>@file.json</c> to read it from a file; null for none.</param>
/// <param name="Yes">Send a method other than GET; without it the request is the plan and nothing is sent.</param>
/// <param name="All">Follow <c>@odata.nextLink</c> and return every page's <c>value</c> in one answer.</param>
/// <param name="Advanced">
/// Send <c>ConsistencyLevel: eventual</c> on the request and its pages: an advanced query on directory objects
/// (<c>$search</c>, <c>$count</c>, a filter on <c>owners/$count</c>), which also needs <c>$count=true</c> in the path.
/// </param>
public sealed record GraphCallOptions(string Method, string Path, bool Beta, string? Body, bool Yes, bool All, bool Advanced = false);

/// <summary>
/// The raw <c>call</c>: any Graph request, the answer unchanged as the envelope's data (D-006); the data is null
/// only when the call failed before an answer. A GET is sent as it is; any other method changes the tenant, so it is
/// a <see cref="Operations.Plan"/> of one change (task 051): <c>--dry-run</c> prints it, no flag prints it and exits
/// 7, <c>--yes</c> sends it through the write guard, which records it under the home folder. The throttling, the
/// request ids and the failures are the client's, as for every lookup.
/// </summary>
public static class GraphCall
{
    private static readonly string[] Methods = ["GET", "POST", "PATCH", "PUT", "DELETE"];

    /// <summary>Runs a GET (the write of a call needs a plan store: the other overload).</summary>
    /// <exception cref="AegisException">A usage error, or the failure of the request.</exception>
    public static Task<CommandResult<JsonElement?>> RunAsync(GraphClient graph, GraphCallOptions options, CancellationToken cancellationToken) =>
        RunAsync(graph, options, null, cancellationToken);

    /// <summary>
    /// Runs the call: a GET is sent as it is; any other method only with <see cref="GraphCallOptions.Yes"/>, through
    /// the write guard with its one-change plan (<see cref="Plan"/>) recorded in <paramref name="store"/>, and the
    /// answer is the data as for a GET. A write without <c>--yes</c> is the plan's (the command line runs
    /// <see cref="PlanGuard"/> on <see cref="Plan"/> instead).
    /// </summary>
    /// <exception cref="AegisException">A usage error, or the failure of the request.</exception>
    public static async Task<CommandResult<JsonElement?>> RunAsync(GraphClient graph, GraphCallOptions options, PlanStore? store, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(options);

        GraphRequest request = Describe(options);
        if (request.Method != HttpMethod.Get)
        {
            if (!options.Yes || store is null)
            {
                throw new InvalidOperationException("a write of call goes through its plan: PlanGuard over GraphCall.Plan");
            }

            GraphResponse? sent = null;
            WritePlan write = Plan(graph, options, response => sent = response);
            CommandResult<Plan> applied = await PlanGuard.RunAsync(write, WriteMode.Apply, store, cancellationToken).ConfigureAwait(false);
            return applied.Ok && sent is not null
                ? CommandResult.Success<JsonElement?>(Parse(sent.Body))
                : CommandResult.Failure<JsonElement?>(applied.Failure!);
        }

        GraphResponse response = await graph.SendAsync(request, cancellationToken).ConfigureAwait(false);
        JsonElement answer = Parse(response.Body);
        if (options.All)
        {
            answer = await FollowAsync(graph, request, answer, cancellationToken).ConfigureAwait(false);
        }

        return CommandResult.Success<JsonElement?>(answer);
    }

    /// <summary>
    /// The plan of a write of <c>call</c> (task 051): one change, the request as it is (the method, the URL, the body),
    /// which the step sends. <c>call</c> reads no current state, so the plan says what is sent, not what changes.
    /// </summary>
    /// <param name="graph">The client the step sends with.</param>
    /// <param name="options">The call; its method is not GET.</param>
    /// <param name="answered">Gets the service's answer once the step sent the request.</param>
    /// <exception cref="AegisException">A usage error in the options.</exception>
    public static WritePlan Plan(GraphClient graph, GraphCallOptions options, Action<GraphResponse>? answered = null)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(options);
        GraphRequest request = Describe(options);
        if (request.Method == HttpMethod.Get)
        {
            throw new AegisException(FailureKind.Usage, "a GET changes nothing and has no plan", "drop --dry-run");
        }

        string url = GraphClient.BuildUri(request).ToString();
        string detail = request.Body is null ? request.Method.Method : $"{request.Method.Method} with the body {request.Body}";
        return new PlanBuilder("call", $"{request.Method.Method} {url}")
            .Change(
                new PlanChange(PlanAction.Send, url, detail, "not sent", "sent"),
                async cancellationToken =>
                {
                    GraphResponse response = await graph.SendAsync(request, cancellationToken).ConfigureAwait(false);
                    answered?.Invoke(response);
                })
            .Warn("call sends the request as it is: it reads no current state, so the plan names the request, not what it changes")
            .Build();
    }

    /// <summary>
    /// The fixture a request of the options would be answered from (task 036): nothing is sent and no sign-in is
    /// needed, so a fixture is authored without running a test to read its name from the failure.
    /// </summary>
    /// <exception cref="AegisException">A usage error in the options.</exception>
    public static CommandResult<FixtureName> FixtureName(GraphCallOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return CommandResult.Success(FixtureGraphTransport.Describe(GraphClient.BuildUri(Describe(options))));
    }

    /// <summary>The request of the options, or a usage error.</summary>
    internal static GraphRequest Describe(GraphCallOptions options)
    {
        string method = options.Method.Trim().ToUpperInvariant();
        if (!Methods.Contains(method, StringComparer.Ordinal))
        {
            throw new AegisException(FailureKind.Usage, $"'{options.Method}' is not a method call sends", $"use one of {string.Join(", ", Methods)}");
        }

        string path = options.Path.Trim();
        GraphVersion version = options.Beta ? GraphVersion.Beta : GraphVersion.V1;
        if (path.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            if (options.Beta && path.Contains("/v1.0/", StringComparison.OrdinalIgnoreCase))
            {
                throw new AegisException(FailureKind.Usage, "the URL names v1.0 and --beta asks for beta", "drop --beta, or write the URL with /beta/");
            }
        }
        else
        {
            foreach (GraphVersion named in Enum.GetValues<GraphVersion>())
            {
                string prefix = "/" + named.Segment();
                if (path.Equals(prefix, StringComparison.OrdinalIgnoreCase) || path.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase))
                {
                    if (options.Beta && named != GraphVersion.Beta)
                    {
                        throw new AegisException(FailureKind.Usage, $"the path names {named.Segment()} and --beta asks for beta", $"drop --beta, or write the path without {prefix}");
                    }

                    version = named;
                    path = path[prefix.Length..];
                    path = path.Length == 0 ? "/" : path;
                    break;
                }
            }
        }

        string? body = ReadBody(options.Body);
        if (body is not null && method == "GET")
        {
            throw new AegisException(FailureKind.Usage, "a GET has no body", "drop --body, or name the method that takes it");
        }

        var request = new GraphRequest(new HttpMethod(method), path)
        {
            Version = version,
            Body = body,
            EventualConsistency = options.Advanced,
            Accept = GraphRequest.IsRawCount(path) ? GraphRequest.PlainText : null,
        };
        _ = GraphClient.BuildUri(request);
        return request;
    }

    private static string? ReadBody(string? body)
    {
        if (body is null)
        {
            return null;
        }

        string text = body;
        if (body.StartsWith('@'))
        {
            string file = body[1..];
            if (!File.Exists(file))
            {
                throw new AegisException(FailureKind.Usage, $"no file '{file}' for --body", "give the JSON inline, or @ and the path of a file that holds it");
            }

            text = File.ReadAllText(file);
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(text);
            return document.RootElement.GetRawText();
        }
        catch (JsonException exception)
        {
            throw new AegisException(FailureKind.Usage, $"--body is not valid JSON: {exception.Message}", "give a JSON object, such as '{\"displayName\":\"Finance\"}'", exception);
        }
    }

    /// <summary>An answer's body as JSON; an empty body (a 204) is a JSON null.</summary>
    private static JsonElement Parse(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return JsonSerializer.SerializeToElement<object?>(null);
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            // Not JSON ($count, a $value download): the text as a JSON string, so the envelope still parses.
            return JsonSerializer.SerializeToElement(body);
        }
    }

    /// <summary>Every page's <c>value</c> appended to the first page's, the next link dropped.</summary>
    private static async Task<JsonElement> FollowAsync(GraphClient graph, GraphRequest request, JsonElement first, CancellationToken cancellationToken)
    {
        if (JsonNode.Parse(first.GetRawText()) is not JsonObject merged || merged["value"] is not JsonArray values)
        {
            return first;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (merged["@odata.nextLink"]?.GetValue<string>() is { } next && seen.Add(next))
        {
            var page = new GraphRequest(HttpMethod.Get, next) { Version = request.Version, EventualConsistency = request.EventualConsistency };
            GraphResponse response = await graph.SendAsync(page, cancellationToken).ConfigureAwait(false);
            merged.Remove("@odata.nextLink");
            if (JsonNode.Parse(response.Body) is JsonObject answer)
            {
                if (answer["value"] is JsonArray more)
                {
                    foreach (JsonNode? item in more.ToList())
                    {
                        more.Remove(item);
                        values.Add(item);
                    }
                }

                if (answer["@odata.nextLink"] is JsonNode link)
                {
                    merged["@odata.nextLink"] = link.DeepClone();
                }
            }
        }

        merged.Remove("@odata.nextLink");
        return JsonSerializer.SerializeToElement(merged);
    }
}
