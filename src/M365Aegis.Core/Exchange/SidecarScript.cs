using System.Security.Cryptography;

namespace M365Aegis.Core.Exchange;

/// <summary>
/// The Exchange Online sidecar script (D-004) as the build left it: a copy is embedded in the Core, another travels
/// next to every exe as <c>sidecar\aegis-exo-sidecar.ps1</c>. The doctor compares the two by SHA-256, so a script
/// changed after the build, which would run with the administrator's Exchange Online token, is a failed check.
/// </summary>
public static class SidecarScript
{
    /// <summary>The script's file name.</summary>
    public const string FileName = "aegis-exo-sidecar.ps1";

    /// <summary>The name of the embedded copy (the <c>LogicalName</c> in the Core's project file).</summary>
    public const string ResourceName = "M365Aegis.Core.Sidecar." + FileName;

    /// <summary>Where the script sits next to the running exe.</summary>
    public static string InstalledPath => Path.Combine(AppContext.BaseDirectory, "sidecar", FileName);

    /// <summary>The bytes of the copy embedded at build time, or null when the resource is missing.</summary>
    public static byte[]? ReadBuildCopy()
    {
        using Stream? stream = typeof(SidecarScript).Assembly.GetManifestResourceStream(ResourceName);
        if (stream is null)
        {
            return null;
        }

        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    /// <summary>The SHA-256 of the bytes, lower-case hex.</summary>
    public static string Sha256(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }
}
