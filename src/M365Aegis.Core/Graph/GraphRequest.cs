namespace M365Aegis.Core.Graph;

/// <summary>The two Graph endpoints a request goes to; v1.0 unless a call asks for beta.</summary>
public enum GraphVersion
{
    /// <summary><c>https://graph.microsoft.com/v1.0</c>, the default.</summary>
    V1,

    /// <summary><c>https://graph.microsoft.com/beta</c>, for what v1.0 does not have yet.</summary>
    Beta,
}

/// <summary>The URL segment of each version.</summary>
public static class GraphVersions
{
    /// <summary>The segment of the version in a URL: <c>v1.0</c> or <c>beta</c>.</summary>
    public static string Segment(this GraphVersion version) => version switch
    {
        GraphVersion.V1 => "v1.0",
        GraphVersion.Beta => "beta",
        _ => throw new ArgumentOutOfRangeException(nameof(version), version, "a Graph version without a segment"),
    };
}

/// <summary>
/// One Graph request as a lookup describes it: the method, the path under the version
/// (<c>/groups?$select=id,displayName</c>, built with <see cref="GraphQuery"/>), the version, and what the client
/// adds for it (the <c>ConsistencyLevel: eventual</c> header of an advanced query, a JSON body). The client turns it
/// into the URL, sends it with the retries and maps a failed answer to the contract's failures. A path may also be
/// an absolute <c>https://graph.microsoft.com/…</c> URL, which is how a page's <c>@odata.nextLink</c> comes back;
/// the bearer token never goes to another host.
/// </summary>
public sealed record GraphRequest
{
    /// <summary>A request with the given method and path.</summary>
    public GraphRequest(HttpMethod method, string path)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Method = method;
        Path = path;
    }

    /// <summary>The HTTP method.</summary>
    public HttpMethod Method { get; }

    /// <summary>The path under the version, starting with <c>/</c>, or an absolute Graph URL.</summary>
    public string Path { get; }

    /// <summary>v1.0 by default; ignored when <see cref="Path"/> is absolute (the URL names its version).</summary>
    public GraphVersion Version { get; init; } = GraphVersion.V1;

    /// <summary>
    /// Sends <c>ConsistencyLevel: eventual</c>, which an advanced query on directory objects needs (<c>$search</c>,
    /// <c>$count</c>, <c>ne</c>, <c>endsWith</c>, a filter on <c>owners/$count</c>); the client keeps it on the pages
    /// that follow.
    /// </summary>
    public bool EventualConsistency { get; init; }

    /// <summary>The JSON body of a write, or null.</summary>
    public string? Body { get; init; }

    /// <summary>
    /// The media type the answer is accepted in: <c>application/json</c> when null; <c>text/plain</c> for a raw
    /// <c>$count</c> segment, which Graph answers as a plain number and refuses (415) when only JSON is accepted.
    /// </summary>
    public string? Accept { get; init; }

    /// <summary>
    /// The <c>Prefer</c> header, or null: <c>hierarchicalsharing</c> on a drive's <c>delta</c> marks only the items whose
    /// sharing differs from their parent's (the snapshot's sharing read, tasks 087 and 092).
    /// </summary>
    public string? Prefer { get; init; }

    /// <summary>The <see cref="Accept"/> of a raw <c>$count</c> request.</summary>
    public const string PlainText = "text/plain";

    /// <summary>True when the path, before its query, ends in a <c>$count</c> segment (<c>/groups/$count</c>).</summary>
    public static bool IsRawCount(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        int query = path.IndexOf('?', StringComparison.Ordinal);
        string bare = query >= 0 ? path[..query] : path;
        return bare.EndsWith("/$count", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The delegated permission the call needs (<c>Group.Read.All</c>), named in the hint of a 403 answer; null
    /// gives the generic hint.
    /// </summary>
    public string? Permission { get; init; }

    /// <summary>A GET of the path.</summary>
    public static GraphRequest Get(string path) => new(HttpMethod.Get, path);
}

/// <summary>Builds the path and query of a request with every value escaped, and OData string literals.</summary>
public static class GraphQuery
{
    /// <summary>
    /// The path with the query parameters appended in the given order, each value escaped
    /// (<c>Path("/groups", ("$filter", "displayName eq 'R&amp;D'"))</c>); a null value leaves its parameter out.
    /// </summary>
    public static string Path(string path, params (string Name, string? Value)[] parameters)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(parameters);
        string[] pairs = parameters
            .Where(parameter => parameter.Value is not null)
            .Select(parameter => $"{parameter.Name}={Uri.EscapeDataString(parameter.Value!)}")
            .ToArray();
        if (pairs.Length == 0)
        {
            return path;
        }

        return path + (path.Contains('?', StringComparison.Ordinal) ? "&" : "?") + string.Join("&", pairs);
    }

    /// <summary>An OData string literal: the value in single quotes, a quote inside doubled (<c>'O''Brien'</c>).</summary>
    public static string Literal(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
    }
}
