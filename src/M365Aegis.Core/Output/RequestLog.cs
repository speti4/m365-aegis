namespace M365Aegis.Core.Output;

/// <summary>
/// The service request ids of one command's calls, in the order they were answered, for the envelope's
/// <see cref="EnvelopeMeta.RequestIds"/> (D-006: a support case names them). The command runner makes one per
/// command and hands it to the work, the Graph client adds an id per answer; a failed command keeps the ids of the
/// calls made before the failure. Safe to add to from several tasks at once.
/// </summary>
public sealed class RequestLog
{
    private readonly List<string> _ids = [];
    private readonly Lock _lock = new();

    /// <summary>Adds an id; an empty one and a repeat are skipped.</summary>
    public void Add(string? requestId)
    {
        if (string.IsNullOrWhiteSpace(requestId))
        {
            return;
        }

        lock (_lock)
        {
            if (!_ids.Contains(requestId, StringComparer.OrdinalIgnoreCase))
            {
                _ids.Add(requestId);
            }
        }
    }

    /// <summary>The ids so far, as a copy.</summary>
    public IReadOnlyList<string> Ids
    {
        get
        {
            lock (_lock)
            {
                return _ids.ToList();
            }
        }
    }
}
