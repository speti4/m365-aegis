using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace M365Aegis.Core.Output;

/// <summary>The error part of an envelope: a stable code, a sentence for a human, and a hint when there is one.</summary>
/// <param name="Code">A stable, machine-readable code (kebab-case), matched by scripts and agents.</param>
/// <param name="Message">One plain sentence.</param>
/// <param name="Hint">What to do about it, or null.</param>
public sealed record EnvelopeError(string Code, string Message, string? Hint = null);

/// <summary>What every answer carries about how it was produced.</summary>
/// <param name="RequestIds">The service request ids of the calls made, for a support case.</param>
/// <param name="ElapsedMs">Wall-clock time of the command, in milliseconds.</param>
/// <param name="Backend">Which backend answered: graph, admin-api, pwsh, local.</param>
public sealed record EnvelopeMeta(IReadOnlyList<string> RequestIds, long ElapsedMs, string Backend);

/// <summary>The names of the backends an envelope's meta reports.</summary>
public static class Backend
{
    /// <summary>No service was called: the doctor, a usage error, the local state.</summary>
    public const string Local = "local";

    /// <summary>Microsoft Graph.</summary>
    public const string Graph = "graph";

    /// <summary>The Exchange Online Admin API.</summary>
    public const string AdminApi = "admin-api";

    /// <summary>The pwsh sidecar running ExchangeOnlineManagement.</summary>
    public const string Pwsh = "pwsh";
}

/// <summary>
/// The JSON envelope of the command line (D-006): every machine-readable answer has this shape, success or not,
/// so a caller parses one thing. <c>ok</c> says which of <c>data</c> and <c>error</c> is set.
/// </summary>
/// <typeparam name="T">The payload type.</typeparam>
public sealed record Envelope<T>(bool Ok, T? Data, EnvelopeError? Error, EnvelopeMeta Meta);

/// <summary>The two ways to make an envelope.</summary>
public static class Envelope
{
    /// <summary>A successful answer.</summary>
    public static Envelope<T> Success<T>(T data, EnvelopeMeta meta) => new(true, data, null, meta);

    /// <summary>A failed answer; <paramref name="data"/> may carry a partial result (a doctor report, a bulk run).</summary>
    public static Envelope<T> Failure<T>(EnvelopeError error, EnvelopeMeta meta, T? data = default) =>
        new(false, data, error, meta);
}

/// <summary>
/// Serializes envelopes the one way the contract names: camelCase, nulls left out, enums as kebab-case strings
/// (<c>"status":"pass"</c>, never a number), one line.
/// </summary>
public static class EnvelopeJson
{
    /// <summary>The options of the contract; the tests read the scrubbed samples back with them.</summary>
    internal static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
        // Accented names and the like stay readable in the output instead of é escapes.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower) },
    };

    /// <summary>The envelope as one JSON line, without a trailing newline.</summary>
    public static string Serialize<T>(Envelope<T> envelope) => JsonSerializer.Serialize(envelope, Options);
}
