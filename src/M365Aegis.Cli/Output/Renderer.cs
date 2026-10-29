using M365Aegis.Core.Output;

namespace M365Aegis.Cli.Output;

/// <summary>
/// The one rendering path of the command line (D-006). A <see cref="CommandResult{T}"/> becomes either the JSON
/// envelope, one line on stdout, or the table: the data on stdout through the result type's
/// <see cref="ITableView{T}"/>, and the failure, if any, as an <c>error:</c> line on stderr. The exit code is
/// the result's. Every line ends in <c>\n</c> on every platform, so the same result gives the same bytes, which
/// the snapshot tests rely on.
/// </summary>
internal static class Renderer
{
    /// <summary>Writes the rendering and returns the exit code the process should end with.</summary>
    internal static ExitCode Render<T>(
        CommandResult<T> result,
        EnvelopeMeta meta,
        ITableView<T> view,
        OutputMode mode,
        TextWriter output,
        TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        if (mode == OutputMode.Json)
        {
            output.Write(EnvelopeJson.Serialize(result.ToEnvelope(meta)));
            output.Write('\n');
            return result.ExitCode;
        }

        if (result.Data is not null && view.Headers.Count > 0)
        {
            output.Write(TableWriter.Render(view.Headers, view.Rows(result.Data).ToList()));
            bool first = true;
            foreach (string line in view.Footer(result.Data))
            {
                if (first)
                {
                    output.Write('\n');
                    first = false;
                }

                output.Write(line);
                output.Write('\n');
            }
        }

        if (result.Failure is { } failure)
        {
            error.Write($"error: {failure.Message} ({failure.Code})\n");
            if (failure.Hint is not null)
            {
                error.Write($"hint: {failure.Hint}\n");
            }
        }

        return result.ExitCode;
    }
}
