using System.Text.Json;
using System.Text.Json.Serialization;

namespace M365Aegis.Core.Graph;

/// <summary>How Graph answers are read: the web defaults (camelCase, case-insensitive names, numbers as strings).</summary>
public static class GraphJson
{
    /// <summary>The options the client deserializes every answer with; a lookup reads a batch body with them too.</summary>
    public static JsonSerializerOptions Options { get; } = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}

/// <summary>
/// The base of the typed records a lookup reads from Graph: the <c>@odata.type</c> of the object, and every field
/// the record does not name kept in <see cref="AdditionalData"/>, so a record never drops what Graph sent (a new
/// property, an extension) and writes it back when it is serialized.
/// </summary>
public abstract record GraphObject
{
    /// <summary>The object's type (<c>#microsoft.graph.user</c>), when Graph sends it.</summary>
    [JsonPropertyName("@odata.type")]
    public string? ODataType { get; init; }

    /// <summary>The fields the record does not name, as Graph sent them.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalData { get; init; }
}

/// <summary>One page of a Graph collection: the items, the link to the next page, and the count when asked for.</summary>
/// <typeparam name="T">The item type.</typeparam>
public sealed record GraphPage<T>
{
    /// <summary>The items of this page.</summary>
    public IReadOnlyList<T> Value { get; init; } = [];

    /// <summary>The absolute URL of the next page, or null on the last one.</summary>
    [JsonPropertyName("@odata.nextLink")]
    public string? NextLink { get; init; }

    /// <summary>The total count of the collection, when the request had <c>$count=true</c>.</summary>
    [JsonPropertyName("@odata.count")]
    public long? Count { get; init; }
}

/// <summary>The error part of a failed Graph answer: <c>{"error":{"code","message"}}</c>.</summary>
internal sealed record GraphErrorBody(GraphErrorDetail? Error);

/// <summary>The code and the message of a Graph error.</summary>
internal sealed record GraphErrorDetail(string? Code, string? Message);
