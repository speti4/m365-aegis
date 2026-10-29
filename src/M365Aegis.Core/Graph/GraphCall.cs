using System.Text.Json;
using System.Text.Json.Nodes;
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
public sealed record GraphCallOptions(string Method, string Path, bool Beta, string? Body, bool Yes, bool All);

/// <summary>
/// The raw <c>call</c>: any Graph request, the answer unchanged as the envelope's data (D-006); the data is null
/// only when the call failed before an answer. A GET is sent as
/// it is; any other method changes the tenant, so without <c>--yes</c> the request itself is returned as the
/// plan and the command exits 7 with nothing sent. The throttling, the request ids and the failures are the
/// client's, as for every lookup.
/// </summary>
public static class GraphCall
{
    private static readonly string[] Methods = ["GET", "POST", "PATCH", "PUT", "DELETE"];

    /// <summary>Runs the call.</summary>
    /// <exception cref="AegisException">A usage error, or the failure of the request.</exception>
    public static async Task<CommandResult<JsonElement?>> RunAsync(GraphClient graph, GraphCallOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(options);

        GraphRequest request = Describe(options);
        if (request.Method != HttpMethod.Get && !options.Yes)
        {
            var plan = new JsonObject
            {
                ["method"] = request.Method.Method,
                ["url"] = GraphClient.BuildUri(request).ToString(),
                ["body"] = request.Body is null ? null : JsonNode.Parse(request.Body),
            };
            return CommandResult.Failure<JsonElement?>(
                new AegisFailure(
                    FailureKind.RefusedWithoutYes,
                    $"{request.Method.Method} changes the tenant; nothing was sent",
                    "check the request in the data and run the command again with --yes"),
                JsonSerializer.SerializeToElement(plan));
        }

        GraphResponse response = await graph.SendAsync(request, cancellationToken).ConfigureAwait(false);
        JsonElement answer = Parse(response.Body);
        if (options.All && request.Method == HttpMethod.Get)
        {
            answer = await FollowAsync(graph, request, answer, cancellationToken).ConfigureAwait(false);
        }

        return CommandResult.Success<JsonElement?>(answer);
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

        var request = new GraphRequest(new HttpMethod(method), path) { Version = version, Body = body };
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
            var page = new GraphRequest(HttpMethod.Get, next) { Version = request.Version };
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
