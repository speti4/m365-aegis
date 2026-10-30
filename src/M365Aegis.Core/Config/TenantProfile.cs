using System.Text.Json;
using System.Text.Json.Serialization;
using M365Aegis.Core.Auth;
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

/// <summary>One profile as <c>profile list</c> shows it.</summary>
/// <param name="Name">The profile's name.</param>
/// <param name="Tenant">The tenant id or domain.</param>
/// <param name="ClientId">The application (client) id.</param>
/// <param name="Account">The user principal name.</param>
/// <param name="IsDefault">Whether a command without <c>--profile</c> uses this one.</param>
public sealed record ProfileSummary(string Name, string Tenant, string ClientId, string Account, bool IsDefault);

/// <summary>The result of <c>profile list</c> and <c>profile use</c>: the file and its profiles.</summary>
/// <param name="File">The path of <c>config.json</c>.</param>
/// <param name="Default">The profile a command without <c>--profile</c> uses, or null when there is none.</param>
/// <param name="Profiles">The profiles, in the file's order.</param>
public sealed record ProfileList(string File, string? Default, IReadOnlyList<ProfileSummary> Profiles);

/// <summary>The result of <c>profile remove</c>.</summary>
/// <param name="Profile">The removed profile's name.</param>
/// <param name="Account">Its user principal name.</param>
/// <param name="SignedOut">Whether its account was signed out first; false when another profile still uses the same app registration and account.</param>
/// <param name="RemovedAccounts">How many account records the sign-out removed.</param>
/// <param name="Default">The default profile afterwards, or null when there is none.</param>
public sealed record ProfileRemoval(string Profile, string Account, bool SignedOut, int RemovedAccounts, string? Default);

/// <summary>
/// Reads and writes the tenant profiles in <c>config.json</c> under the home folder:
/// <c>{"default":"work","profiles":{"work":{"tenant":…,"clientId":…,"account":…}}}</c>. A missing file is an
/// empty store; a missing profile is an <see cref="FailureKind.Auth"/> failure whose hint is the login command.
/// </summary>
public sealed class ProfileStore
{
    /// <summary>The name a profile gets when <c>login</c> is run without <c>--profile</c>.</summary>
    public const string DefaultProfileName = "default";

    /// <summary>The login command with its flags, the command line's hint of every failure that needs a profile.</summary>
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
    private readonly SignInHints _hints;

    /// <summary>A store over the given home folder (<see cref="AegisHome.Resolve"/> for the real one).</summary>
    /// <param name="home">The home folder.</param>
    /// <param name="hints">What a missing or broken profile tells the person to do; the command line's when null (task 046).</param>
    public ProfileStore(string home, SignInHints? hints = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(home);
        _home = home;
        _hints = hints ?? SignInHints.CommandLine;
    }

    /// <summary>The path of the file, whether or not it exists.</summary>
    public string Path => AegisHome.ConfigPath(_home);

    /// <summary>The hint of a failure that names a profile the file does not have.</summary>
    public const string ListHint = "`m365-aegis profile list` shows the names";

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
                file.Profiles.Count == 0 ? _hints.NoProfile : _hints.NoDefaultProfile);
        }

        if (!file.Profiles.TryGetValue(key, out ProfileEntry? entry))
        {
            throw new AegisException(FailureKind.Auth, $"no profile named '{key}' in {Path}", _hints.NoProfile);
        }

        return entry.ToProfile(key);
    }

    /// <summary>Every profile in the file, in the file's order; empty when there is no file.</summary>
    public IReadOnlyList<TenantProfile> List() =>
        Load().Profiles.Select(pair => pair.Value.ToProfile(pair.Key)).ToList();

    /// <summary>The name of the default profile, or null.</summary>
    public string? DefaultName => Load().Default;

    /// <summary>
    /// The profiles with the one a command without <c>--profile</c> uses marked: the file's <c>default</c> when it
    /// names a profile, else the only profile when there is one.
    /// </summary>
    public ProfileList Describe()
    {
        ConfigFile file = Load();
        string? effective = EffectiveDefault(file);
        return new ProfileList(
            Path,
            effective,
            file.Profiles.Select(pair => new ProfileSummary(pair.Key, pair.Value.Tenant, pair.Value.ClientId, pair.Value.Account, pair.Key == effective)).ToList());
    }

    /// <summary>Makes the named profile the default (<c>profile use</c>).</summary>
    /// <exception cref="AegisException">A <see cref="FailureKind.NotFound"/> failure when there is no such profile.</exception>
    public ProfileList Use(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ConfigFile file = Load();
        if (!file.Profiles.ContainsKey(name))
        {
            throw NoSuchProfile(name);
        }

        file.Default = name;
        Write(file);
        return Describe();
    }

    /// <summary>
    /// Deletes the named profile from the file and returns the default afterwards. When the default was removed, the
    /// only profile left becomes the default; with several left none does, so a command never signs in to a tenant
    /// nobody chose.
    /// </summary>
    /// <exception cref="AegisException">A <see cref="FailureKind.NotFound"/> failure when there is no such profile.</exception>
    public string? Remove(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ConfigFile file = Load();
        if (!file.Profiles.Remove(name))
        {
            throw NoSuchProfile(name);
        }

        if (file.Default == name)
        {
            file.Default = file.Profiles.Count == 1 ? file.Profiles.Keys.Single() : null;
        }

        Write(file);
        return EffectiveDefault(file);
    }

    /// <summary>The named profile, or a <see cref="FailureKind.NotFound"/> failure (the commands that manage profiles).</summary>
    public TenantProfile Find(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return Load().Profiles.TryGetValue(name, out ProfileEntry? entry) ? entry.ToProfile(name) : throw NoSuchProfile(name);
    }

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

        Write(file);
    }

    private static string? EffectiveDefault(ConfigFile file) =>
        file.Default is { } named && file.Profiles.ContainsKey(named) ? named
        : file.Default is null && file.Profiles.Count == 1 ? file.Profiles.Keys.Single()
        : null;

    private AegisException NoSuchProfile(string name) =>
        new(FailureKind.NotFound, $"no profile named '{name}' in {Path}", ListHint);

    private void Write(ConfigFile file)
    {
        AegisHome.Create(_home);
        File.WriteAllText(Path, JsonSerializer.Serialize(file, FileOptions) + "\n");
    }

    private ConfigFile Load()
    {
        if (!File.Exists(Path))
        {
            return new ConfigFile();
        }

        ConfigFile file;
        try
        {
            file = JsonSerializer.Deserialize<ConfigFile>(File.ReadAllText(Path), FileOptions) ?? new ConfigFile();
        }
        catch (JsonException exception)
        {
            throw new AegisException(FailureKind.Auth, $"{Path} is not valid JSON", _hints.BrokenProfileFile, exception);
        }

        // A hand-edited file may hold a profile without one of its three values: the file's fault, not the tool's.
        foreach ((string name, ProfileEntry? entry) in file.Profiles)
        {
            if (entry is null || string.IsNullOrWhiteSpace(entry.Tenant) || string.IsNullOrWhiteSpace(entry.ClientId) || string.IsNullOrWhiteSpace(entry.Account))
            {
                throw new AegisException(
                    FailureKind.Auth,
                    $"the profile '{name}' in {Path} lacks its tenant, clientId or account",
                    _hints.IncompleteProfile);
            }
        }

        return file;
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
