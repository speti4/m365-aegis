using System.Globalization;
using System.Text;
using System.Text.Json;

namespace M365Aegis.Core.Graph;

/// <summary>
/// Answers Graph requests from recorded, scrubbed answers on disk (tests/fixtures/graph): the URL mirrored as the
/// path, so <c>GET https://graph.microsoft.com/v1.0/groups?$select=id</c> is answered by
/// <c>graph.microsoft.com/v1.0/groups@$select=id.json</c>. The rules of the name (<see cref="RelativePath(Uri)"/>):
/// the query parameters are decoded and sorted, joined with <c>&amp;</c> after an <c>@</c>, and a character a file
/// name cannot hold is written as <c>%XX</c>. A <c>.headers</c> file next to the answer adds a <c>Status: 429</c>
/// line and response headers (<c>Retry-After: 1</c>, <c>request-id: …</c>); the n-th time the same URL is asked,
/// <c>&lt;name&gt;.try-n.json</c> and <c>.try-n.headers</c> answer when they exist, which is how a throttled
/// request and its retry are recorded. A URL without a fixture gets a 404 whose message names the missing file.
/// A <c>$batch</c> call without a recorded answer of its own is answered item by item, each item from its own
/// fixture (task 041), and its <c>.headers</c> file, when there is one, gives the batch call's headers. An Exchange
/// Online Admin API call (a POST to <see cref="AdminApiHost"/>) is named by its URL and its cmdlet's parameters
/// (task 042). An answer that is not JSON (a usage report's CSV) is a <c>.csv</c> file. No token is asked for. The tests use it in process, and the command line uses it when
/// <see cref="GraphConnection.FixturesEnvironmentVariable"/> names a folder.
/// </summary>
public sealed class FixtureGraphTransport : IGraphTransport
{
    private static readonly char[] Unsafe = ['\\', '/', ':', '*', '?', '"', '<', '>', '|', '%'];

    private readonly string _root;
    private readonly Dictionary<string, int> _calls = new(StringComparer.Ordinal);
    private readonly List<GraphHttpRequest> _requests = [];
    private readonly List<GraphHttpRequest> _batchItems = [];
    private readonly Lock _lock = new();

    /// <summary>A transport over the folder that holds the hosts (<c>tests/fixtures/graph</c>).</summary>
    public FixtureGraphTransport(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        _root = root;
    }

    /// <summary>Every request sent so far, in order, for the tests' assertions.</summary>
    public IReadOnlyList<GraphHttpRequest> Requests
    {
        get
        {
            lock (_lock)
            {
                return _requests.ToList();
            }
        }
    }

    /// <summary>The items of every <c>$batch</c> call answered item by item, in order, for the tests' assertions.</summary>
    public IReadOnlyList<GraphHttpRequest> BatchItems
    {
        get
        {
            lock (_lock)
            {
                return _batchItems.ToList();
            }
        }
    }

    /// <inheritdoc/>
    public Task<GraphResponse> SendAsync(GraphHttpRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (_lock)
        {
            _requests.Add(request);
        }

        return AnswerAsync(request, batchItem: false, cancellationToken);
    }

    private async Task<GraphResponse> AnswerAsync(GraphHttpRequest request, bool batchItem, CancellationToken cancellationToken)
    {
        string relative = RelativePath(request.Uri, request.Body);
        int attempt;
        lock (_lock)
        {
            if (batchItem)
            {
                _batchItems.Add(request);
            }

            attempt = _calls.GetValueOrDefault(relative) + 1;
            _calls[relative] = attempt;
        }

        // The full name first; a name too long for a checkout under a deep folder is stored in its short form.
        string shortRelative = ShortRelativePath(request.Uri, request.Body);
        string? chosen = null;
        foreach (string candidate in new[] { relative, shortRelative }.Distinct(StringComparer.Ordinal))
        {
            string basePath = Path.Combine(_root, candidate.Replace('/', Path.DirectorySeparatorChar));
            string tryPath = $"{basePath}.try-{attempt.ToString(CultureInfo.InvariantCulture)}";
            chosen = Exists(tryPath) ? tryPath : Exists(basePath) ? basePath : null;
            if (chosen is not null)
            {
                break;
            }
        }

        bool batch = !batchItem && request.Method == HttpMethod.Post && request.Uri.AbsolutePath.EndsWith("/$batch", StringComparison.Ordinal);
        if (chosen is null && !batch)
        {
            string name = shortRelative == relative ? relative : shortRelative;
            string message = $"no fixture for {request.Method} {request.Uri}: {name}.json";
            string body = $"{{\"error\":{{\"code\":\"fixtureMissing\",\"message\":{JsonSerializer.Serialize(message)}}}}}";
            return new GraphResponse(404, [], body);
        }

        int status = 200;
        var headers = new List<KeyValuePair<string, string>>();
        string headersFile = chosen + ".headers";
        if (chosen is not null && File.Exists(headersFile))
        {
            foreach (string line in await File.ReadAllLinesAsync(headersFile, cancellationToken).ConfigureAwait(false))
            {
                int colon = line.IndexOf(':', StringComparison.Ordinal);
                if (colon <= 0)
                {
                    continue;
                }

                string name = line[..colon].Trim();
                string value = line[(colon + 1)..].Trim();
                if (string.Equals(name, "Status", StringComparison.OrdinalIgnoreCase))
                {
                    status = int.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);
                }
                else
                {
                    headers.Add(new(name, value));
                }
            }
        }

        string? file = chosen is null ? null : new[] { chosen + ".json", chosen + ".csv" }.FirstOrDefault(File.Exists);
        if (file is null && batch)
        {
            // No recorded answer for the batch itself: each item is answered from its own fixture, as one request.
            return new GraphResponse(status, headers, await AnswerItemsAsync(request, cancellationToken).ConfigureAwait(false));
        }

        string content = file is not null ? await File.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false) : "";
        return new GraphResponse(status, headers, content);
    }

    /// <summary>
    /// The body of a <c>$batch</c> answer built from its items' own fixtures: each item's URL under the batch's
    /// version, answered as a request of its own (its <c>.try-n</c> files included), in the order the items came.
    /// </summary>
    private async Task<string> AnswerItemsAsync(GraphHttpRequest batch, CancellationToken cancellationToken)
    {
        string root = batch.Uri.AbsoluteUri[..batch.Uri.AbsoluteUri.LastIndexOf("/$batch", StringComparison.Ordinal)];
        // The client always sends a $batch with its body (GraphClient.BatchAsync).
        using JsonDocument document = JsonDocument.Parse(batch.Body!);
        var responses = new List<object>();
        foreach (JsonElement item in document.RootElement.GetProperty("requests").EnumerateArray())
        {
            string url = item.GetProperty("url").GetString() ?? "/";
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (item.TryGetProperty("headers", out JsonElement itemHeaders) && itemHeaders.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty header in itemHeaders.EnumerateObject())
                {
                    headers[header.Name] = header.Value.GetString() ?? "";
                }
            }

            string? body = item.TryGetProperty("body", out JsonElement itemBody) ? itemBody.GetRawText() : null;
            var request = new GraphHttpRequest(new HttpMethod(item.GetProperty("method").GetString() ?? "GET"), new Uri(root + url), headers, body);
            GraphResponse answer = await AnswerAsync(request, batchItem: true, cancellationToken).ConfigureAwait(false);
            JsonElement? parsed = null;
            if (!string.IsNullOrWhiteSpace(answer.Body))
            {
                using JsonDocument answerBody = JsonDocument.Parse(answer.Body);
                parsed = answerBody.RootElement.Clone();
            }

            responses.Add(new Dictionary<string, object?>
            {
                ["id"] = item.GetProperty("id").GetString(),
                ["status"] = answer.StatusCode,
                ["headers"] = answer.Headers,
                ["body"] = parsed,
            });
        }

        return JsonSerializer.Serialize(new Dictionary<string, object> { ["responses"] = responses });
    }

    /// <summary>
    /// The fixture's path for a URL, relative to the root, with <c>/</c> separators and without the extension:
    /// the host, the decoded path, and the sorted, decoded query after an <c>@</c>
    /// (<c>graph.microsoft.com/v1.0/groups@$filter=displayName eq 'Finance'&amp;$select=id</c>). An <c>@</c> inside
    /// the path (a function's address parameter) is written as <c>%40</c>, so the first <c>@</c> of a name is always
    /// the query's.
    /// </summary>
    public static string RelativePath(Uri uri) => RelativePath(uri, null);

    /// <summary>
    /// The fixture's path for a request: <see cref="RelativePath(Uri)"/>, and for an Exchange Online Admin API call
    /// (a POST to <see cref="AdminApiHost"/>, whose URL is the same for every read of an endpoint) the parameters of
    /// its <c>CmdletInput</c> body joined to the query, sorted with it: <c>Identity=finance@contoso.com</c>,
    /// <c>ResultSize=Unlimited</c>, a list as its items joined with a comma (task 042); for a Graph <c>$ref</c> write
    /// the id its body points at, as <c>@odata.id=&lt;id&gt;</c> (task 052).
    /// </summary>
    public static string RelativePath(Uri uri, string? body)
    {
        ArgumentNullException.ThrowIfNull(uri);
        string[] segments = uri.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(segment => Safe(Uri.UnescapeDataString(segment)).Replace("@", "%40", StringComparison.Ordinal))
            .ToArray();
        string path = string.Join("/", [Safe(uri.Host.ToLowerInvariant()), .. segments]);
        string query = uri.Query.TrimStart('?');
        List<(string Name, string Value)> pairs = query
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair =>
            {
                int equals = pair.IndexOf('=', StringComparison.Ordinal);
                // A query string writes a space as + (a next link from Graph may), so + reads as a space.
                string name = Uri.UnescapeDataString((equals < 0 ? pair : pair[..equals]).Replace('+', ' '));
                string value = equals < 0 ? "" : Uri.UnescapeDataString(pair[(equals + 1)..].Replace('+', ' '));
                return (Name: name, Value: value);
            })
            .ToList();
        if (string.Equals(uri.Host, AdminApiHost, StringComparison.OrdinalIgnoreCase))
        {
            pairs.AddRange(CmdletParameters(body));
        }
        else if (Reference(body) is { } reference)
        {
            pairs.Add(("@odata.id", reference));
        }

        if (pairs.Count == 0)
        {
            return path;
        }

        IEnumerable<string> sorted = pairs
            .OrderBy(pair => pair.Name, StringComparer.Ordinal)
            .ThenBy(pair => pair.Value, StringComparer.Ordinal)
            .Select(pair => $"{pair.Name}={pair.Value}");
        return $"{path}@{Safe(string.Join("&", sorted))}";
    }

    /// <summary>
    /// The object a <c>$ref</c> write's body points at (<c>{"@odata.id":"https://graph.microsoft.com/v1.0/directoryObjects/{id}"}</c>,
    /// task 052), as the last segment of its URL, or null for any other body: two members added to one group are two
    /// requests to the same URL, told apart by it.
    /// </summary>
    private static string? Reference(string? body)
    {
        if (string.IsNullOrWhiteSpace(body) || !body.Contains("@odata.id", StringComparison.Ordinal))
        {
            return null;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("@odata.id", out JsonElement value)
                && value.ValueKind == JsonValueKind.String
                && value.GetString() is { Length: > 0 } link
                ? link[(link.TrimEnd('/').LastIndexOf('/') + 1)..].TrimEnd('/')
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The host of the Exchange Online Admin API, whose POST bodies name their fixtures too.</summary>
    public const string AdminApiHost = "outlook.office365.com";

    /// <summary>The parameters of an Admin API call's <c>CmdletInput</c>, as name and text.</summary>
    private static List<(string Name, string Value)> CmdletParameters(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return [];
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("CmdletInput", out JsonElement input)
                || !input.TryGetProperty("Parameters", out JsonElement parameters)
                || parameters.ValueKind != JsonValueKind.Object)
            {
                return [];
            }

            return parameters.EnumerateObject().Select(parameter => (parameter.Name, Text(parameter.Value))).ToList();
        }
        catch (JsonException)
        {
            return [];
        }

        static string Text(JsonElement value) => value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? "",
            JsonValueKind.Array => string.Join(",", value.EnumerateArray().Select(Text)),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => value.GetRawText(),
        };
    }

    /// <summary>
    /// The fixture's path in the form a file system holds under a deep checkout: the same as
    /// <see cref="RelativePath(Uri)"/> when the file name (the last segment with its query) has at most
    /// <see cref="MaxNameLength"/> characters and the whole path at most <see cref="MaxPathLength"/>, else the
    /// segment, the first 60 characters of the query and a <c>~</c> with the first 8 hex digits of the full query's
    /// SHA-256 (<c>messageTraces@$filter=receivedDateTime ge 2026-10-18T10%3A00%3A00Z and rec~1a2b3c4d</c>). The
    /// transport looks for the full name first and this one second; a missing fixture's message names this one.
    /// </summary>
    public static string ShortRelativePath(Uri uri) => ShortRelativePath(uri, null);

    /// <summary>The short form of <see cref="RelativePath(Uri, string)"/>: an Admin API call's body counts as its query.</summary>
    public static string ShortRelativePath(Uri uri, string? body)
    {
        string relative = RelativePath(uri, body);
        int slash = relative.LastIndexOf('/');
        string name = relative[(slash + 1)..];
        int at = name.IndexOf('@', StringComparison.Ordinal);
        if ((name.Length <= MaxNameLength && relative.Length <= MaxPathLength) || at < 0)
        {
            return relative;
        }

        string query = name[(at + 1)..];
        string hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(query)))[..8];
        return $"{relative[..(slash + 1)]}{name[..(at + 1)]}{query[..Math.Min(60, query.Length)]}~{hash}";
    }

    /// <summary>The longest file name (without the extension) kept whole by <see cref="ShortRelativePath(Uri)"/>.</summary>
    public const int MaxNameLength = 120;

    /// <summary>
    /// The longest path under the fixture root (without the extension) kept whole by <see cref="ShortRelativePath(Uri)"/>:
    /// with <c>tests/fixtures/graph/</c> in front and <c>.try-1.headers</c> behind it, a file stays within the
    /// <see cref="MaxRepoPathLength"/> characters from the repo root that <c>scripts/check-fixtures.py</c> holds.
    /// </summary>
    public const int MaxPathLength = 150;

    /// <summary>
    /// The longest path from the repo root a fixture may have (<c>scripts/check-fixtures.py</c> holds it): git for
    /// Windows refuses a path over 260 characters without <c>core.longpaths</c>, and a worktree under
    /// <c>.claude/worktrees</c> puts about 70 characters in front of the repo's own path.
    /// </summary>
    public const int MaxRepoPathLength = 190;

    /// <summary>The fixture root's path from the repo root, with the separator behind it.</summary>
    public const string RepoFolder = "tests/fixtures/graph/";

    /// <summary>
    /// Where the answer to a URL is stored, for authoring a fixture (<c>m365-aegis call GET &lt;path&gt;
    /// --fixture-name</c>): the full and the short name, and the file a new fixture is written to, which is the
    /// short form whenever it differs from the full name.
    /// </summary>
    public static FixtureName Describe(Uri uri)
    {
        string full = RelativePath(uri);
        string stored = ShortRelativePath(uri);
        string file = $"{RepoFolder}{stored}.json";
        return new FixtureName(uri.AbsoluteUri, full, stored, file, file.Length, file.Length <= MaxRepoPathLength);
    }

    private static bool Exists(string basePath) =>
        File.Exists(basePath + ".json") || File.Exists(basePath + ".csv") || File.Exists(basePath + ".headers");

    /// <summary>A name with the characters a file name cannot hold (and <c>%</c> itself) written as <c>%XX</c>.</summary>
    private static string Safe(string name)
    {
        var builder = new StringBuilder(name.Length);
        foreach (char character in name)
        {
            if (character < ' ' || Array.IndexOf(Unsafe, character) >= 0)
            {
                builder.Append('%').Append(((int)character).ToString("X2", CultureInfo.InvariantCulture));
            }
            else
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }
}

/// <summary>The name of a URL's fixture, as <see cref="FixtureGraphTransport.Describe"/> computes it.</summary>
/// <param name="Url">The absolute URL.</param>
/// <param name="FullName">The full name under the fixture root, without the extension (<see cref="FixtureGraphTransport.RelativePath(Uri)"/>).</param>
/// <param name="StoredName">The name a new fixture is stored under: the short form when the full one is too long (<see cref="FixtureGraphTransport.ShortRelativePath(Uri)"/>).</param>
/// <param name="File">The answer's file from the repo root (<c>tests/fixtures/graph/…json</c>); a <c>.headers</c> or <c>.try-n.json</c> goes next to it.</param>
/// <param name="Length">The length of <paramref name="File"/>.</param>
/// <param name="Fits">Whether <paramref name="File"/> stays within <see cref="FixtureGraphTransport.MaxRepoPathLength"/> characters.</param>
public sealed record FixtureName(string Url, string FullName, string StoredName, string File, int Length, bool Fits);
