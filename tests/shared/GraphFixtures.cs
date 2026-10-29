using M365Aegis.Core.Graph;

namespace M365Aegis.Tests;

/// <summary>
/// The recorded Graph answers under tests/fixtures/graph, read from the source tree (the names carry <c>$</c>,
/// <c>@</c> and <c>%XX</c>, which the build's copy step would mangle), and a client over them that never waits:
/// the throttling delays are recorded instead of slept. Linked into both test projects.
/// </summary>
internal static class GraphFixtures
{
    /// <summary>The folder that holds the hosts: the repo's tests/fixtures/graph.</summary>
    internal static string Root { get; } = FindRoot();

    /// <summary>A fixture transport and a client over it; <paramref name="delays"/> collects every wait.</summary>
    internal static (GraphClient Client, FixtureGraphTransport Transport) Client(List<TimeSpan>? delays = null, GraphClientOptions? options = null)
    {
        var transport = new FixtureGraphTransport(Root);
        GraphClientOptions settings = (options ?? new GraphClientOptions()) with
        {
            Delay = (delay, _) =>
            {
                delays?.Add(delay);
                return Task.CompletedTask;
            },
            Jitter = () => 0,
        };
        return (new GraphClient(transport, options: settings), transport);
    }

    private static string FindRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            string candidate = Path.Combine(directory.FullName, "tests", "fixtures", "graph");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException($"no tests/fixtures/graph above {AppContext.BaseDirectory}");
    }
}
