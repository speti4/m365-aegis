using System.Reflection;

namespace M365Aegis.Core;

/// <summary>What the exes say about themselves: the one version of Directory.Build.props and the product name.</summary>
public static class ProductInfo
{
    /// <summary>The product name, also the folder name under the local application data.</summary>
    public const string Name = "m365-aegis";

    /// <summary>The version of Directory.Build.props, as the assembly carries it (without build metadata).</summary>
    public static string Version { get; } = ReadVersion();

    private static string ReadVersion()
    {
        string? informational = typeof(ProductInfo).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrEmpty(informational))
        {
            return typeof(ProductInfo).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        }

        // The SDK appends "+<commit>" to the informational version; the exes print the plain version.
        int plus = informational.IndexOf('+', StringComparison.Ordinal);
        return plus < 0 ? informational : informational[..plus];
    }
}
