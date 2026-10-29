namespace M365Aegis.Cli.Output;

/// <summary>
/// How one result type is shown as a table (D-006): the columns, one row per item, and the lines under the table.
/// A command defines a view for its result type next to itself; the envelope needs none, it serializes the result
/// as it is.
/// </summary>
/// <typeparam name="T">The result type of the command.</typeparam>
internal interface ITableView<in T>
{
    /// <summary>The column headers; empty when the result has no table (a plain failure).</summary>
    IReadOnlyList<string> Headers { get; }

    /// <summary>
    /// The column headers for this data; <see cref="Headers"/> by default. A view whose columns follow the data
    /// (the raw <c>call</c>, whose answer can be any shape) overrides it.
    /// </summary>
    IReadOnlyList<string> HeadersFor(T data) => Headers;

    /// <summary>The rows, each with as many cells as there are headers.</summary>
    IEnumerable<IReadOnlyList<string>> Rows(T data);

    /// <summary>Lines printed under the table, after a blank line; none by default.</summary>
    IEnumerable<string> Footer(T data) => [];
}

/// <summary>The view of a result that has no table: nothing is printed for the data.</summary>
/// <typeparam name="T">The result type.</typeparam>
internal sealed class NoTableView<T> : ITableView<T>
{
    /// <summary>The one instance.</summary>
    internal static NoTableView<T> Instance { get; } = new();

    private NoTableView()
    {
    }

    public IReadOnlyList<string> Headers => [];

    public IEnumerable<IReadOnlyList<string>> Rows(T data) => [];
}
