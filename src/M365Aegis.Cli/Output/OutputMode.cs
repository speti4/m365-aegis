namespace M365Aegis.Cli.Output;

/// <summary>How a command prints its answer (D-006).</summary>
internal enum OutputMode
{
    /// <summary>The JSON envelope, one line, for pipes and agents.</summary>
    Json,

    /// <summary>A table for a person at a terminal.</summary>
    Table,
}
