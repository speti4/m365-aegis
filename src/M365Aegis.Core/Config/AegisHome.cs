namespace M365Aegis.Core.Config;

/// <summary>
/// Where the tool keeps what is not in the repo: the tenant profiles (config.json), the MSAL cache, the WebView2
/// data folder, the logs and the plan records (D-005). <c>%LOCALAPPDATA%\m365-aegis\</c> by default;
/// <c>M365AEGIS_HOME</c> overrides it, which the tests use to point it at a temporary folder.
/// </summary>
public static class AegisHome
{
    /// <summary>The environment variable that overrides the home folder.</summary>
    public const string EnvironmentVariable = "M365AEGIS_HOME";

    /// <summary>The home folder, resolved but not created.</summary>
    public static string Resolve()
    {
        string? overridden = Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(overridden))
        {
            return Path.GetFullPath(overridden);
        }

        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, ProductInfo.Name);
    }

    /// <summary>The path of the tenant profiles file under the home folder.</summary>
    public static string ConfigPath(string home) => Path.Combine(home, "config.json");
}
