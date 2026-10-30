using System.Text.Json;
using M365Aegis.Core.Graph;

namespace M365Aegis.Tests;

/// <summary>
/// The requests a fixture transport wrote to its request log (task 071: <see cref="FixtureGraphTransport.RequestLog"/>,
/// one JSON line per request with the method, the URL and the body), read for what is not a read. Shared by the write
/// guard of the command line (task 071) and the window's (task 079). Linked into both test projects.
/// </summary>
internal static class SentRequests
{
    /// <summary>
    /// Every request of the log lines that is not a read: a GET reads, a <c>$batch</c> call reads when each of its items
    /// is a GET (the user lookups go twenty to one), and an Admin API call reads when its cmdlet is a <c>Get-</c> one
    /// (every Admin API call is a POST).
    /// </summary>
    internal static List<string> Writes(IEnumerable<string> lines)
    {
        var writes = new List<string>();
        foreach (string line in lines.Where(line => line.Length > 0))
        {
            using JsonDocument document = JsonDocument.Parse(line);
            string method = document.RootElement.GetProperty("method").GetString()!;
            var uri = new Uri(document.RootElement.GetProperty("url").GetString()!);
            string? body = document.RootElement.GetProperty("body").GetString();
            if (method == "GET")
            {
                continue;
            }

            if (method == "POST" && uri.AbsolutePath.EndsWith("/$batch", StringComparison.Ordinal))
            {
                using JsonDocument batch = JsonDocument.Parse(body ?? "{}");
                writes.AddRange(batch.RootElement.GetProperty("requests").EnumerateArray()
                    .Where(item => item.GetProperty("method").GetString() != "GET")
                    .Select(item => $"$batch item {item.GetProperty("method").GetString()} {item.GetProperty("url").GetString()}"));
                continue;
            }

            if (method == "POST" && uri.Host == FixtureGraphTransport.AdminApiHost)
            {
                using JsonDocument call = JsonDocument.Parse(body ?? "{}");
                string? cmdlet = call.RootElement.GetProperty("CmdletInput").GetProperty("CmdletName").GetString();
                if (cmdlet?.StartsWith("Get-", StringComparison.Ordinal) != true)
                {
                    writes.Add($"Admin API {cmdlet}");
                }

                continue;
            }

            writes.Add($"{method} {uri}");
        }

        return writes;
    }
}
