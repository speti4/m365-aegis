using M365Aegis.Core.Auth;

namespace M365Aegis.Cli.Output;

/// <summary>
/// The one thing a command writes outside the renderer: the device-code sentence (the page to open, the code to
/// type), on stderr, so stdout stays the envelope or the table and a redirected shell still shows it.
/// </summary>
internal static class DeviceCodePrompts
{
    /// <summary>Prints the service's sentence on stderr.</summary>
    internal static Task OnStderr(DeviceCodePrompt prompt)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        Console.Error.Write(prompt.Message);
        Console.Error.Write('\n');
        return Task.CompletedTask;
    }
}
