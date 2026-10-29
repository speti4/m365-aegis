using System.Globalization;
using System.Text.Json;

namespace M365Aegis.Core.Exchange.Sidecar;

/// <summary>
/// One cmdlet run as the sidecar's <c>invoke</c> message carries it: the cmdlet, its parameters (strings, numbers,
/// booleans, string lists; a switch is <c>true</c>), the properties to keep of each object (all of them when
/// null), and the values piped into it (the bulk form of the <c>Get-EXO*</c> cmdlets).
/// </summary>
/// <param name="Cmdlet">A cmdlet of <see cref="ExoCmdlets.Allowed"/>.</param>
public sealed record ExoCommand(string Cmdlet)
{
    /// <summary>The parameters, by name.</summary>
    public IReadOnlyDictionary<string, object?> Parameters { get; init; } = new Dictionary<string, object?>();

    /// <summary>The properties to keep of each object; null keeps every property.</summary>
    public IReadOnlyList<string>? Properties { get; init; }

    /// <summary>The values piped into the cmdlet, or null.</summary>
    public IReadOnlyList<string>? Input { get; init; }

    /// <summary>The <c>data</c> of the <c>invoke</c> message.</summary>
    internal Dictionary<string, object?> ToMessageData()
    {
        var data = new Dictionary<string, object?> { ["cmdlet"] = Cmdlet, ["parameters"] = Parameters };
        if (Properties is not null)
        {
            data["properties"] = Properties;
        }

        if (Input is not null)
        {
            data["input"] = Input;
        }

        return data;
    }
}

/// <summary>What a cmdlet run returned: the objects, and the non-terminating errors of a run that still returned some.</summary>
/// <param name="Items">The objects, each a flat set of plain values.</param>
/// <param name="Errors">The error messages of the items that failed (a bulk run), in the order they came.</param>
public sealed record ExoResult(IReadOnlyList<ExoObject> Items, IReadOnlyList<string> Errors);

/// <summary>
/// One object a cmdlet returned, as the sidecar flattens it: every value is a string, a number, a boolean, null or
/// a list of strings. The typed records of the lookups are read from it through these accessors, so a property the
/// service renamed or dropped is a null, never an exception.
/// </summary>
public sealed class ExoObject
{
    private readonly Dictionary<string, JsonElement> _values;

    /// <summary>An object over the JSON object the sidecar sent.</summary>
    public ExoObject(JsonElement element)
    {
        _values = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in element.EnumerateObject())
            {
                _values[property.Name] = property.Value.Clone();
            }
        }
    }

    /// <summary>The property names, as the sidecar sent them.</summary>
    public IEnumerable<string> Names => _values.Keys;

    /// <summary>A value as it came, or null when missing: an Admin API answer may nest an object (task 042).</summary>
    internal JsonElement? Value(string name) => _values.TryGetValue(name, out JsonElement value) ? value : null;

    /// <summary>A value as text: null when missing or null or empty, a list joined with a comma.</summary>
    public string? Text(string name)
    {
        if (!_values.TryGetValue(name, out JsonElement value))
        {
            return null;
        }

        string? text = value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => value.GetRawText(),
            JsonValueKind.Array => string.Join(", ", Texts(name)),
            _ => null,
        };
        return string.IsNullOrEmpty(text) ? null : text;
    }

    /// <summary>A value as a list of strings: a single value is a list of one, null or missing an empty list.</summary>
    public IReadOnlyList<string> Texts(string name)
    {
        if (!_values.TryGetValue(name, out JsonElement value))
        {
            return [];
        }

        if (value.ValueKind == JsonValueKind.Array)
        {
            return value.EnumerateArray()
                .Select(element => element.ValueKind == JsonValueKind.String ? element.GetString() : element.ValueKind == JsonValueKind.Null ? null : element.GetRawText())
                .Where(text => !string.IsNullOrEmpty(text))
                .Select(text => text!)
                .ToList();
        }

        string? single = Text(name);
        return single is null ? [] : [single];
    }

    /// <summary>A value as a boolean (<c>true</c>, or the text <c>True</c>), or null.</summary>
    public bool? Flag(string name)
    {
        if (!_values.TryGetValue(name, out JsonElement value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(value.GetString(), out bool parsed) => parsed,
            _ => null,
        };
    }

    /// <summary>A value as a whole number, or null.</summary>
    public long? Number(string name)
    {
        if (!_values.TryGetValue(name, out JsonElement value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number)
        {
            return value.TryGetInt64(out long whole) ? whole : (long)value.GetDouble();
        }

        return value.ValueKind == JsonValueKind.String
            && long.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed)
            ? parsed
            : null;
    }
}
