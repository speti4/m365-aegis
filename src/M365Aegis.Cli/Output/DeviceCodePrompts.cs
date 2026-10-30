using System.Diagnostics.CodeAnalysis;
using M365Aegis.Core.Auth;

namespace M365Aegis.Cli.Output;

/// <summary>
/// The one thing a command writes outside the renderer: the device-code sentence (the page to open, the code to
/// type), on stderr, so stdout stays the envelope or the table and a redirected shell still shows it.
/// </summary>
internal static class DeviceCodePrompts
{
    /// <summary>Prints the service's sentence on stderr.</summary>
    [ExcludeFromCodeCoverage(Justification = "the console of a device-code sign-in, which needs the real sign-in service; Write is tested (docs/development.md, Coverage)")]
    internal static Task OnStderr(DeviceCodePrompt prompt) => Write(Console.Error, prompt);

    /// <summary>Prints the service's sentence and a line end on <paramref name="writer"/>.</summary>
    internal static Task Write(TextWriter writer, DeviceCodePrompt prompt)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(prompt);
        writer.Write(prompt.Message);
        writer.Write('\n');
        return Task.CompletedTask;
    }
}
