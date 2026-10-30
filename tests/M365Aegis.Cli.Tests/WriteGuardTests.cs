using System.CommandLine;
using System.Text.Json;
using M365Aegis.Core.Graph;
using M365Aegis.Core.Operations;
using M365Aegis.Core.Output;
using M365Aegis.Tests;

namespace M365Aegis.Cli.Tests;

/// <summary>
/// The write guard over the whole command tree (task 071, D-006, SECURITY.md "Writes"): every command that carries
/// <c>--dry-run</c> and <c>--yes</c> is a write, found by walking the command line's own tree, and each one runs as a
/// child process without <c>--yes</c> (no flag, and <c>--dry-run</c>) from its arguments, from <c>--from-csv</c> and from
/// <c>--resume</c> when it has them, over every layer at once: the recorded Graph and Admin API answers, whose transport
/// writes each request to the file <see cref="FixtureGraphTransport.RequestLogVariable"/> names, and the fake Exchange
/// Online module, which writes each write cmdlet to the file <c>M365AEGIS_FAKE_EXO_WRITES</c> names. A run passes when
/// the plan is the answer (exit 7 and <c>refused</c>, or 0 and <c>dry-run</c> or <c>up-to-date</c>), no record is
/// written, and nothing but reads was sent: GETs, <c>$batch</c> calls of GETs and Admin API calls of <c>Get-</c> cmdlets.
/// </summary>
public sealed class WriteGuardTests
{
    /// <summary>
    /// What each write command is run with: its arguments (a plan with at least one change, which the resume needs) and
    /// a sample file of <c>tests/fixtures/csv</c> for <c>--from-csv</c>. A new write command found in the tree fails
    /// <see cref="EveryWriteCommandHasItsInputs"/> until it has a line here.
    /// </summary>
    private static readonly Dictionary<string, WriteInputs> Inputs = new(StringComparer.Ordinal)
    {
        ["call"] = new(["DELETE", "/groups/00000000-0000-0000-0000-000000000031"], null),
        ["group members add"] = new(["Finance", "dave@contoso.com"], "group-members-add.csv"),
        ["group members remove"] = new(["Finance", "carol@contoso.com"], "group-members-add.csv"),
        ["group owners add"] = new(["Finance", "bob@contoso.com"], "group-members-add.csv"),
        ["group owners remove"] = new(["Finance", "alice@contoso.com"], "group-members-add.csv"),
        ["mailbox access grant"] = new(["finance", "--user", "bob@contoso.com", "--right", "FullAccess"], "mailbox-grants.csv"),
        ["mailbox access revoke"] = new(["finance", "--user", "alice@contoso.com", "--right", "FullAccess"], "mailbox-grants.csv"),
        ["mailbox forwarding set"] = new(["alice", "--to", "alice@fabrikam.com"], "mailbox-forwarding-set.csv"),
        ["mailbox forwarding clear"] = new(["bob"], "mailbox-forwarding-clear.csv"),
        // A write of local files only (task 066): the home holds the three sample records, which must all survive.
        ["plan prune"] = new(["--older-than", "1"], null) { Seeded = true },
    };

    /// <summary>The write commands of the tree, by their path (<c>group members add</c>).</summary>
    public static TheoryData<string> WriteCommandPaths => [.. WriteCommands().Keys];

    [Fact]
    public void EveryWriteCommandHasItsInputs()
    {
        Dictionary<string, Command> writes = WriteCommands();

        Assert.Equal(Inputs.Keys.Order(StringComparer.Ordinal), writes.Keys.Order(StringComparer.Ordinal));
        foreach ((string path, Command command) in writes)
        {
            Assert.True(Has(command, "--from-csv") == Inputs[path].Csv is not null, $"'{path}' and its --from-csv sample disagree");
        }
    }

    [Theory]
    [MemberData(nameof(WriteCommandPaths))]
    public async Task AWriteWithoutYesSendsNothing(string path)
    {
        Command command = WriteCommands()[path];
        Assert.True(Inputs.TryGetValue(path, out WriteInputs? inputs), $"the write command '{path}' has no inputs in WriteGuardTests.Inputs");
        string[] words = path.Split(' ');
        var run = new GuardRun(inputs.Seeded);

        // The runs of one phase are independent, so they run at once; the sinks are read when all of them are done.
        List<Task<JsonElement>> runs =
        [
            Task.Run(() => run.Expect([.. words, .. inputs.Arguments], dryRun: false, plan => Assert.NotEqual(0, plan.GetProperty("changes").GetArrayLength()))),
            Task.Run(() => run.Expect([.. words, .. inputs.Arguments], dryRun: true)),
        ];
        if (inputs.Csv is { } csv)
        {
            runs.Add(Task.Run(() => run.Expect([.. words, "--from-csv", PlanSamples.Csv(csv)], dryRun: false)));
            runs.Add(Task.Run(() => run.Expect([.. words, "--from-csv", PlanSamples.Csv(csv)], dryRun: true)));
        }

        JsonElement[] plans = await Task.WhenAll(runs);
        run.AssertNothingSent(0);
        if (Has(command, "--resume"))
        {
            // A record cut short while applying the plan the arguments make: every change is left to do.
            string record = run.CutShort(plans[0], ["--json", .. words, .. inputs.Arguments, "--yes"]);
            await Task.WhenAll(
                Task.Run(() => run.Expect([.. words, "--resume", record], dryRun: false)),
                Task.Run(() => run.Expect([.. words, "--resume", record], dryRun: true)));
            run.AssertNothingSent(1);
        }
    }

    [Theory]
    [MemberData(nameof(WriteCommandPaths))]
    public async Task AWriteWithoutArgumentsSendsNothing(string path)
    {
        // The part a new write command gets without an edit: whatever it answers without its inputs, nothing is sent.
        var run = new GuardRun();

        int[] exitCodes = await Task.WhenAll(
            new[] { Array.Empty<string>(), ["--dry-run"] }.Select(flags => Task.Run(() => run.Run([.. path.Split(' '), .. flags]).ExitCode)));

        Assert.All(exitCodes, exitCode => Assert.True(exitCode is 0 or 2 or 7, $"{path}: exit {exitCode}"));
        run.AssertNothingSent(0);
    }

    [Theory]
    [InlineData("POST", "/groups", "{\"displayName\":\"Made Up\"}")]
    [InlineData("PATCH", "/groups/00000000-0000-0000-0000-000000000031", "{\"description\":\"made up\"}")]
    [InlineData("PUT", "/groups/00000000-0000-0000-0000-000000000031/team", "{}")]
    [InlineData("delete", "/groups/00000000-0000-0000-0000-000000000031", null)]
    public async Task ARawCallOfEveryWriteMethodSendsNothing(string method, string path, string? body)
    {
        var run = new GuardRun();
        string[] arguments = body is null ? ["call", method, path] : ["call", method, path, "--body", body];

        await Task.WhenAll(
            Task.Run(() => run.Expect(arguments, dryRun: false, plan => Assert.Equal("send", plan.GetProperty("changes")[0].GetProperty("action").GetString()))),
            Task.Run(() => run.Expect(arguments, dryRun: true)));
        run.AssertNothingSent(0);
    }

    /// <summary>The commands of the tree that carry both guard flags, by their path under the root.</summary>
    internal static Dictionary<string, Command> WriteCommands()
    {
        var writes = new Dictionary<string, Command>(StringComparer.Ordinal);
        void Walk(Command command, string path)
        {
            if (Has(command, "--dry-run") && Has(command, "--yes"))
            {
                writes[path] = command;
            }

            foreach (Command child in command.Subcommands)
            {
                Walk(child, path.Length == 0 ? child.Name : $"{path} {child.Name}");
            }
        }

        Walk(Program.Build().Root, "");
        return writes;
    }

    internal static bool Has(Command command, string option) => command.Options.Any(candidate => candidate.Name == option);

    /// <summary>The inputs of one write command.</summary>
    /// <param name="Arguments">The arguments after the command's path.</param>
    /// <param name="Csv">The sample file under <c>tests/fixtures/csv</c> for <c>--from-csv</c>, or null when the command has none.</param>
    private sealed record WriteInputs(string[] Arguments, string? Csv)
    {
        /// <summary>Whether the home starts with the three sample records (<see cref="PlanRecordSamples"/>), for a write over them.</summary>
        public bool Seeded { get; init; }
    }

    /// <summary>
    /// The runs of one test: a temporary home, and a folder of sinks where each run gets its own request log and fake
    /// module's write file (two processes appending to one file at once would collide).
    /// </summary>
    internal sealed class GuardRun
    {
        private readonly string _home = PlanSamples.TempHome();
        private readonly string _sinks = Path.Combine(Path.GetTempPath(), "m365-aegis-tests", $"sinks-{Guid.NewGuid():N}");

        /// <summary>The records the home starts with.</summary>
        private readonly int _seeded;

        /// <summary>A run over a new home; <paramref name="seeded"/> writes the three sample records into it first.</summary>
        internal GuardRun(bool seeded = false)
        {
            Directory.CreateDirectory(_sinks);
            if (seeded)
            {
                PlanRecordSamples.Write(_home);
                _seeded = Directory.GetFiles(Path.Combine(_home, "plans")).Length;
            }
        }

        /// <summary>Runs the command line over every layer at once, as JSON.</summary>
        internal (int ExitCode, string Stdout, string Stderr) Run(string[] arguments)
        {
            string id = Guid.NewGuid().ToString("N");
            Dictionary<string, string> environment = MailboxCommandTests.FakeExchange;
            environment[GraphConnection.FixturesEnvironmentVariable] = GraphFixtures.Root;
            environment[FixtureGraphTransport.RequestLogVariable] = Path.Combine(_sinks, $"{id}-requests.jsonl");
            environment["M365AEGIS_FAKE_EXO_WRITES"] = Path.Combine(_sinks, $"{id}-exchange-writes.jsonl");
            environment["M365AEGIS_HOME"] = _home;
            return CliTests.RunWith(environment, ["--json", .. arguments]);
        }

        /// <summary>
        /// Runs a write without <c>--yes</c> and holds the answer to the guard: the plan as the data, exit 7 and
        /// <c>refused</c> without a flag, exit 0 and <c>dry-run</c> under <c>--dry-run</c> (or 0 and <c>up-to-date</c>
        /// either way). Returns the plan. The sinks are read by <see cref="AssertNothingSent"/> once the runs of a phase
        /// are done, since runs of one phase go at once.
        /// </summary>
        internal JsonElement Expect(string[] arguments, bool dryRun, Action<JsonElement>? also = null)
        {
            (int exitCode, string stdout, string stderr) = Run(dryRun ? [.. arguments, "--dry-run"] : arguments);
            string what = $"m365-aegis --json {string.Join(' ', arguments)}{(dryRun ? " --dry-run" : "")}";

            Assert.True(stdout.StartsWith('{'), $"{what}: exit {exitCode}, no envelope: {stdout}{stderr}");
            using JsonDocument document = JsonDocument.Parse(stdout);
            JsonElement root = document.RootElement;
            Assert.True(root.TryGetProperty("data", out JsonElement plan) && plan.TryGetProperty("changes", out _), $"{what}: exit {exitCode}, no plan: {stdout}");
            string? mode = plan.GetProperty("mode").GetString();
            if (mode == "up-to-date")
            {
                Assert.Equal(0, exitCode);
            }
            else if (dryRun)
            {
                Assert.True(exitCode == 0 && mode == "dry-run", $"{what}: exit {exitCode}, mode {mode}");
            }
            else
            {
                Assert.True(exitCode == 7 && mode == "refused", $"{what}: exit {exitCode}, mode {mode}");
                Assert.Equal("refused-without-yes", root.GetProperty("error").GetProperty("code").GetString());
            }

            also?.Invoke(plan);
            return plan.Clone();
        }

        /// <summary>Nothing but reads in the request log, no write cmdlet in the fake module's file, no record but the test's own.</summary>
        internal void AssertNothingSent(int records)
        {
            Assert.Empty(Writes());
            string exchangeWrites = string.Concat(Directory.GetFiles(_sinks, "*-exchange-writes.jsonl").Select(File.ReadAllText)).Trim();
            Assert.True(exchangeWrites.Length == 0, $"the fake module recorded a write: {exchangeWrites}");
            string plans = Path.Combine(_home, "plans");
            // The records the home started with are all there still (a prune without --yes removes none), and no new one.
            Assert.Equal(_seeded + records, Directory.Exists(plans) ? Directory.GetFiles(plans).Length : 0);
        }

        /// <summary>A record of <paramref name="plan"/> left at <c>applying</c> in the home folder, as an apply cut short leaves it; its name.</summary>
        internal string CutShort(JsonElement plan, IReadOnlyList<string> arguments)
        {
            Plan read = JsonSerializer.Deserialize<Plan>(plan.GetRawText(), EnvelopeJson.Options)!;
            var store = new PlanStore(_home, new PlanRecordContext("command line", null, arguments));
            return Path.GetFileName(store.Begin(read with { Mode = PlanMode.Applying, Started = DateTimeOffset.UtcNow }));
        }

        /// <summary>Every request of the log that is not a read.</summary>
        private List<string> Writes()
        {
            var writes = new List<string>();
            foreach (string line in Directory.GetFiles(_sinks, "*-requests.jsonl").SelectMany(File.ReadAllLines).Where(line => line.Length > 0))
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
}
