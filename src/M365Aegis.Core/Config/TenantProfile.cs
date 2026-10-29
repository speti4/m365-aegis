using System.Text.Json;
using System.Text.Json.Serialization;
using M365Aegis.Core.Output;

namespace M365Aegis.Core.Config;

/// <summary>
/// One tenant profile of <c>config.json</c>: which app registration signs in, in which tenant, as whom (D-003). The
/// values are the maintainer's and never enter the repo; the file is written by <c>m365-aegis login</c> and may be
/// edited by hand.
/// </summary>
/// <param name="Name">The profile's name, the key in the file (<c>default</c> when none was given).</param>
/// <param name="Tenant">The tenant id or a verified domain (<c>contoso.onmicrosoft.com</c>), the authority's tenant.</param>
/// <param name="ClientId">The application (client) id of the public-client app registration.</param>
/// <param name="Account">The user principal name of the account to sign in; the broker may list several.</param>
public sealed record TenantProfile(string Name, string Tenant, string ClientId, string Account);

/// <summary>
/// Reads and writes the tenant profiles in <c>config.json</c> under the home folder:
/// <c>{"default":"work","profiles":{"work":{"tenant":…,"clientId":…,"account":…}}}</c>. A missing file is an
/// empty store; a missing profile is an <see cref="FailureKind.Auth"/> failure whose hint is the login command.
/// </summary>
public sealed class ProfileStore
{
    /// <summary>The name a profile gets when <c>login</c> is run without <c>--profile</c>.</summary>
    public const string DefaultProfileName = "default";

    /// <summary>The login command with its flags, the hint of every failure that needs a profile.</summary>
    public const string LoginHint =
        "run `m365-aegis login --tenant <tenant id or domain> --client-id <application id> --account <user@domain>` once";

    private static readonly JsonSerializerOptions FileOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        // A person edits this file, so it is indented, and a key they typed is matched regardless of its case.
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly string _home;

    /// <summary>A store over the given home folder (<see cref="AegisHome.Resolve"/> for the real one).</summary>
    public ProfileStore(string home)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(home);
        _home = home;
    }

    /// <summary>The path of the file, whether or not it exists.</summary>
    public string Path => AegisHome.ConfigPath(_home);

    /// <summary>The named profile, or the default one when <paramref name="name"/> is null.</summary>
    /// <exception cref="AegisException">No such profile, no default, or the file cannot be read.</exception>
    public TenantProfile Resolve(string? name)
    {
        ConfigFile file = Load();
        string? key = name ?? file.Default ?? (file.Profiles.Count == 1 ? file.Profiles.Keys.Single() : null);
        if (key is null)
        {
            throw new AegisException(
                FailureKind.Auth,
                file.Profiles.Count == 0 ? $"no tenant profile in {Path}" : $"no default profile in {Path}",
                file.Profiles.Count == 0 ? LoginHint : "name one with --profile, or set \"default\" in the file");
        }

        if (!file.Profiles.TryGetValue(key, out ProfileEntry? entry))
        {
            throw new AegisException(FailureKind.Auth, $"no profile named '{key}' in {Path}", LoginHint);
        }

        return entry.ToProfile(key);
    }

    /// <summary>Every profile in the file, in the file's order; empty when there is no file.</summary>
    public IReadOnlyList<TenantProfile> List() =>
        Load().Profiles.Select(pair => pair.Value.ToProfile(pair.Key)).ToList();

    /// <summary>The name of the default profile, or null.</summary>
    public string? DefaultName => Load().Default;

    /// <summary>
    /// Writes or replaces a profile and makes it the default when there is none yet (or when asked). Creates the
    /// home folder and the file on first use.
    /// </summary>
    public void Save(TenantProfile profile, bool makeDefault = false)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ConfigFile file = Load();
        file.Profiles[profile.Name] = ProfileEntry.From(profile);
        if (makeDefault || file.Default is null)
        {
            file.Default = profile.Name;
        }

        Directory.CreateDirectory(_home);
        File.WriteAllText(Path, JsonSerializer.Serialize(file, FileOptions) + "\n");
    }

    private ConfigFile Load()
    {
        if (!File.Exists(Path))
        {
            return new ConfigFile();
        }

        try
        {
            return JsonSerializer.Deserialize<ConfigFile>(File.ReadAllText(Path), FileOptions) ?? new ConfigFile();
        }
        catch (JsonException exception)
        {
            throw new AegisException(FailureKind.Auth, $"{Path} is not valid JSON", "fix the file, or delete it and run `m365-aegis login` again", exception);
        }
    }

    /// <summary>The file as it is on disk; the profiles are keyed by name.</summary>
    private sealed class ConfigFile
    {
        public string? Default { get; set; }

        public Dictionary<string, ProfileEntry> Profiles { get; init; } = new(StringComparer.Ordinal);
    }

    /// <summary>One profile without its name, which is the key.</summary>
    private sealed record ProfileEntry(string Tenant, string ClientId, string Account)
    {
        public static ProfileEntry From(TenantProfile profile) => new(profile.Tenant, profile.ClientId, profile.Account);

        public TenantProfile ToProfile(string name) => new(name, Tenant, ClientId, Account);
    }
}
