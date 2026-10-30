using System.CommandLine;
using System.Text;
using System.Text.RegularExpressions;
using M365Aegis.Core.Graph;
using M365Aegis.Tests;

namespace M365Aegis.Cli.Tests;

/// <summary>
/// The usage guide the release zip ships as <c>GUIDE.md</c> (<c>docs/usage.md</c>, task 086) against the product: every
/// command of the help tree is named in it, every card of the window has its section, and every example of its
/// PowerShell blocks runs over the recorded answers and the fake Exchange Online module. A new command or card fails
/// here until the guide has it.
/// </summary>
public sealed partial class UsageGuideTests
{
    /// <summary>
    /// The examples that need the real sign-in or the network, which the recorded answers cannot stand in for: the
    /// sign-in commands (the Windows account manager), a profile's removal (it signs the account out) and the doctor's
    /// online run (the endpoints). Every other example runs.
    /// </summary>
    private static readonly string[][] NotRun =
    [
        ["login"], ["logout"], ["whoami"], ["profile", "remove"], ["--verbose", "login"],
    ];

    private static readonly string Guide = File.ReadAllText(Path.Combine(RepoRoot(), "docs", "usage.md"));

    /// <summary>Every command of the help tree, by its path (<c>mailbox access grant</c>); hidden ones are left out.</summary>
    public static TheoryData<string> CommandPaths
    {
        get
        {
            var paths = new TheoryData<string>();
            void Walk(Command command, string path)
            {
                foreach (Command child in command.Subcommands.Where(child => !child.Hidden))
                {
                    string childPath = path.Length == 0 ? child.Name : $"{path} {child.Name}";
                    paths.Add(childPath);
                    Walk(child, childPath);
                }
            }

            Walk(Program.Build().Root, "");
            return paths;
        }
    }

    /// <summary>The titles of the window's cards, as the front end's components write them.</summary>
    public static TheoryData<string> CardTitles
    {
        get
        {
            var titles = new TheoryData<string>();
            foreach (string file in Directory.GetFiles(Path.Combine(RepoRoot(), "gui", "web", "src", "components"), "*.tsx").Order(StringComparer.Ordinal))
            {
                string source = File.ReadAllText(file);
                foreach (Match match in CardTitle().Matches(source).Concat(WriteCardTitle().Matches(source)))
                {
                    titles.Add(match.Groups["title"].Value.Trim());
                }
            }

            return titles;
        }
    }

    /// <summary>The examples of the guide's PowerShell blocks: one command line each, without its comment.</summary>
    public static TheoryData<string> Examples
    {
        get
        {
            var examples = new TheoryData<string>();
            bool inBlock = false;
            foreach (string line in Guide.ReplaceLineEndings("\n").Split('\n'))
            {
                if (line.StartsWith("```", StringComparison.Ordinal))
                {
                    inBlock = line == "```powershell";
                    continue;
                }

                if (inBlock && line.StartsWith("m365-aegis ", StringComparison.Ordinal))
                {
                    examples.Add(CommandOnly(line));
                }
            }

            return examples;
        }
    }

    [Fact]
    public void TheGuideHasCommandsCardsAndExamplesToCheck()
    {
        Assert.True(CommandPaths.Count >= 40, $"{CommandPaths.Count} commands");
        Assert.Equal(["Sign-in", "Group members", "Mailbox access", "Mailbox rights", "Mailbox forwarding"],
            CardTitles.Select(row => row.Data).OrderBy(Order));
        Assert.True(Examples.Count >= 40, $"{Examples.Count} examples");
    }

    [Theory]
    [MemberData(nameof(CommandPaths))]
    public void EveryCommandIsInTheGuide(string path) =>
        Assert.True(
            Regex.IsMatch(Guide, $@"m365-aegis {Regex.Escape(path)}(?![\w-])"),
            $"docs/usage.md never names `m365-aegis {path}`: add it to its section, with an example");

    [Theory]
    [MemberData(nameof(CardTitles))]
    public void EveryCardOfTheWindowHasItsSection(string title) =>
        Assert.True(
            Guide.ReplaceLineEndings("\n").Split('\n').Contains($"### {title}"),
            $"docs/usage.md has no section `### {title}` under \"The window\"");

    [Theory]
    [MemberData(nameof(Examples))]
    public void EveryExampleRunsOverTheFixtures(string example)
    {
        string[] words = Split(example);
        Assert.Equal("m365-aegis", words[0]);
        string[] args = words[1..];
        if (NotRun.Any(prefix => args.Take(prefix.Length).SequenceEqual(prefix)) || (args[0] == "doctor" && !args.Contains("--offline")))
        {
            return;
        }

        bool write = args.Contains("--dry-run") || args.Contains("--yes");
        // A write is planned, never applied, here: the write-guard tests cover the apply. A CSV file is the sample's.
        string[] run = [.. args.Select(arg => arg == "--yes" ? "--dry-run" : arg)];
        int csv = Array.IndexOf(run, "--from-csv");
        if (csv >= 0)
        {
            run[csv + 1] = PlanSamples.Csv(run[csv + 1]);
        }

        (int exitCode, string stdout, string stderr) = CliTests.RunWith(Layers(profiles: args[0] == "profile"), ["--json", .. run]);

        Assert.True(stdout.StartsWith('{'), $"{example}: exit {exitCode}, no envelope: {stdout}{stderr}");
        Assert.True(exitCode == 0 || (exitCode == 7 && !write), $"{example}: exit {exitCode}: {stdout}{stderr}");
    }

    /// <summary>The order the guide gives the cards: the window's own, top to bottom.</summary>
    private static int Order(string title) => Array.IndexOf(["Sign-in", "Group members", "Mailbox access", "Mailbox rights", "Mailbox forwarding"], title);

    /// <summary>
    /// The recorded Graph and Admin API answers, the fake Exchange Online module, and a home with the three sample plan
    /// records (the resume and <c>plan show</c> examples name them); the profile examples get a home with the profiles
    /// <c>work</c> and <c>lab</c> instead.
    /// </summary>
    private static Dictionary<string, string> Layers(bool profiles)
    {
        string home = PlanSamples.TempHome();
        Directory.CreateDirectory(home);
        if (profiles)
        {
            File.WriteAllText(Path.Combine(home, "config.json"), """
                {"default":"work","profiles":{
                  "work":{"tenant":"contoso.onmicrosoft.com","clientId":"00000000-0000-0000-0000-000000000010","account":"alice@contoso.com"},
                  "lab":{"tenant":"fabrikam.onmicrosoft.com","clientId":"00000000-0000-0000-0000-000000000011","account":"alice@fabrikam.com"}}}
                """);
        }
        else
        {
            PlanRecordSamples.Write(home);
        }

        Dictionary<string, string> environment = MailboxCommandTests.FakeExchange;
        environment[GraphConnection.FixturesEnvironmentVariable] = GraphFixtures.Root;
        environment["M365AEGIS_HOME"] = home;
        return environment;
    }

    /// <summary>An example line without its trailing comment and without what it pipes into.</summary>
    private static string CommandOnly(string line)
    {
        char quote = '\0';
        for (int index = 0; index < line.Length; index++)
        {
            char current = line[index];
            if (quote != '\0')
            {
                quote = current == quote ? '\0' : quote;
            }
            else if (current is '"' or '\'')
            {
                quote = current;
            }
            else if ((current == '#' || current == '|') && index > 0 && line[index - 1] == ' ')
            {
                return line[..index].TrimEnd();
            }
        }

        return line.TrimEnd();
    }

    /// <summary>The words of a PowerShell command line: spaces separate them, single or double quotes hold one.</summary>
    private static string[] Split(string line)
    {
        var words = new List<string>();
        var word = new StringBuilder();
        char quote = '\0';
        bool any = false;
        foreach (char current in line)
        {
            if (quote != '\0')
            {
                if (current == quote)
                {
                    quote = '\0';
                }
                else
                {
                    word.Append(current);
                }
            }
            else if (current is '"' or '\'')
            {
                quote = current;
                any = true;
            }
            else if (current == ' ')
            {
                if (any || word.Length > 0)
                {
                    words.Add(word.ToString());
                    word.Clear();
                    any = false;
                }
            }
            else
            {
                word.Append(current);
            }
        }

        if (any || word.Length > 0)
        {
            words.Add(word.ToString());
        }

        return [.. words];
    }

    private static string RepoRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "docs", "usage.md")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("no docs/usage.md above the test's folder");
    }

    [GeneratedRegex(@"<CardTitle[^>]*>\s*(?<title>[A-Z][^<{]*?)\s*[<{]")]
    private static partial Regex CardTitle();

    [GeneratedRegex(@"<WriteCard\s+title=""(?<title>[^""]+)""")]
    private static partial Regex WriteCardTitle();
}
