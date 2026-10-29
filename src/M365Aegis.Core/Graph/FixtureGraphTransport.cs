using System.Globalization;
using System.Text;
using System.Text.Json;

namespace M365Aegis.Core.Graph;

/// <summary>
/// Answers Graph requests from recorded, scrubbed answers on disk (tests/fixtures/graph): the URL mirrored as the
/// path, so <c>GET https://graph.microsoft.com/v1.0/groups?$select=id</c> is answered by
/// <c>graph.microsoft.com/v1.0/groups@$select=id.json</c>. The rules of the name (<see cref="RelativePath"/>):
/// the query parameters are decoded and sorted, joined with <c>&amp;</c> after an <c>@</c>, and a character a file
/// name cannot hold is written as <c>%XX</c>. A <c>.headers</c> file next to the answer adds a <c>Status: 429</c>
/// line and response headers (<c>Retry-After: 1</c>, <c>request-id: …</c>); the n-th time the same URL is asked,
/// <c>&lt;name&gt;.try-n.json</c> and <c>.try-n.headers</c> answer when they exist, which is how a throttled
/// request and its retry are recorded. A URL without a fixture gets a 404 whose message names the missing file.
/// No token is asked for. The tests use it in process, and the command line uses it when
/// <see cref="GraphConnection.FixturesEnvironmentVariable"/> names a folder.
/// </summary>
public sealed class FixtureGraphTransport : IGraphTransport
{
    private static readonly char[] Unsafe = ['\\', '/', ':', '*', '?', '"', '<', '>', '|', '%'];

    private readonly string _root;
    private readonly Dictionary<string, int> _calls = new(StringComparer.Ordinal);
    private readonly List<GraphHttpRequest> _requests = [];
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

    /// <inheritdoc/>
    public async Task<GraphResponse> SendAsync(GraphHttpRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        string relative = RelativePath(request.Uri);
        int attempt;
        lock (_lock)
        {
            _requests.Add(request);
            attempt = _calls.GetValueOrDefault(relative) + 1;
            _calls[relative] = attempt;
        }

        // The full name first; a name too long for a checkout under a deep folder is stored in its short form.
        string shortRelative = ShortRelativePath(request.Uri);
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

        string jsonFile = chosen + ".json";
        string headersFile = chosen + ".headers";
        if (chosen is null)
        {
            string name = shortRelative == relative ? relative : shortRelative;
            string message = $"no fixture for {request.Method} {request.Uri}: {name}.json";
            string body = $"{{\"error\":{{\"code\":\"fixtureMissing\",\"message\":{JsonSerializer.Serialize(message)}}}}}";
            return new GraphResponse(404, [], body);
        }

        int status = 200;
        var headers = new List<KeyValuePair<string, string>>();
        if (File.Exists(headersFile))
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

        string content = File.Exists(jsonFile) ? await File.ReadAllTextAsync(jsonFile, cancellationToken).ConfigureAwait(false) : "";
        return new GraphResponse(status, headers, content);
    }

    /// <summary>
    /// The fixture's path for a URL, relative to the root, with <c>/</c> separators and without the extension:
    /// the host, the decoded path, and the sorted, decoded query after an <c>@</c>
    /// (<c>graph.microsoft.com/v1.0/groups@$filter=displayName eq 'Finance'&amp;$select=id</c>).
    /// </summary>
    public static string RelativePath(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        string[] segments = uri.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(segment => Safe(Uri.UnescapeDataString(segment)))
            .ToArray();
        string path = string.Join("/", [Safe(uri.Host.ToLowerInvariant()), .. segments]);
        string query = uri.Query.TrimStart('?');
        if (query.Length == 0)
        {
            return path;
        }

        IEnumerable<string> pairs = query
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair =>
            {
                int equals = pair.IndexOf('=', StringComparison.Ordinal);
                // A query string writes a space as + (a next link from Graph may), so + reads as a space.
                string name = Uri.UnescapeDataString((equals < 0 ? pair : pair[..equals]).Replace('+', ' '));
                string value = equals < 0 ? "" : Uri.UnescapeDataString(pair[(equals + 1)..].Replace('+', ' '));
                return (Name: name, Value: value);
            })
            .OrderBy(pair => pair.Name, StringComparer.Ordinal)
            .ThenBy(pair => pair.Value, StringComparer.Ordinal)
            .Select(pair => $"{pair.Name}={pair.Value}");
        return $"{path}@{Safe(string.Join("&", pairs))}";
    }

    /// <summary>
    /// The fixture's path in the form a file system holds under a deep checkout: the same as
    /// <see cref="RelativePath"/> when the file name (the last segment with its query) has at most
    /// <see cref="MaxNameLength"/> characters, else the segment, the first 60 characters of the query and a
    /// <c>~</c> with the first 8 hex digits of the full query's SHA-256
    /// (<c>messageTraces@$filter=receivedDateTime ge 2026-10-18T10%3A00%3A00Z and rec~1a2b3c4d</c>). The
    /// transport looks for the full name first and this one second; a missing fixture's message names this one.
    /// </summary>
    public static string ShortRelativePath(Uri uri)
    {
        string relative = RelativePath(uri);
        int slash = relative.LastIndexOf('/');
        string name = relative[(slash + 1)..];
        int at = name.IndexOf('@', StringComparison.Ordinal);
        if (name.Length <= MaxNameLength || at < 0)
        {
            return relative;
        }

        string query = name[(at + 1)..];
        string hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(query)))[..8];
        return $"{relative[..(slash + 1)]}{name[..(at + 1)]}{query[..Math.Min(60, query.Length)]}~{hash}";
    }

    /// <summary>The longest file name (without the extension) kept whole by <see cref="ShortRelativePath"/>.</summary>
    public const int MaxNameLength = 120;

    private static bool Exists(string basePath) => File.Exists(basePath + ".json") || File.Exists(basePath + ".headers");

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
