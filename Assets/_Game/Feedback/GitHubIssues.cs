using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Game.Feedback
{
    /// <summary>
    /// Files saved tickets as GitHub issues with the GitHub CLI (gh), signed in as whoever runs the game: the screenshot,
    /// context.json and attachments are uploaded to the repository's "feedback-attachments" release (a pre-release made
    /// on first use, so the screenshot shows in the issue), then the issue is opened with the "feedback" label and the
    /// ticket's text pointing at them. Writes github.json into the ticket's folder; a ticket without one is still to be
    /// filed (<see cref="Pending"/>), so one saved offline, or before gh was signed in, goes with the next.
    /// </summary>
    public static class GitHubIssues
    {
        public const string Release = "feedback-attachments";
        public const string Label = "feedback";
        public const string FiledFile = "github.json";

        /// <summary>The repository when the game can't read it from its checkout's origin.</summary>
        public const string DefaultRepo = "JamesPiskorz/game-3";

        public sealed class Outcome
        {
            public string Folder, Url, Error;
            public bool Filed => Url != null;
        }

        /// <summary>"owner/repo" from a GitHub remote URL (https or ssh), or null for anything else.</summary>
        public static string RepoFromRemote(string url)
        {
            Match match = Regex.Match((url ?? "").Trim(), @"github\.com[:/]+([^/\s]+)/([^/\s]+?)(?:\.git)?/?$");
            return match.Success ? $"{match.Groups[1].Value}/{match.Groups[2].Value}" : null;
        }

        /// <summary>The repository the checkout at <paramref name="repoRoot"/> pushes to, else <see cref="DefaultRepo"/>.</summary>
        public static string Repo(string repoRoot)
        {
            if (repoRoot == null) return DefaultRepo;
            Shell.Result remote = Shell.Run("git", repoRoot, new[] { "remote", "get-url", "origin" }, 10000);
            return remote.Ok ? RepoFromRemote(remote.Output) ?? DefaultRepo : DefaultRepo;
        }

        /// <summary>Ticket folders under <paramref name="root"/> not yet filed, oldest first.</summary>
        public static List<string> Pending(string root) =>
            !Directory.Exists(root) ? new List<string>()
                : Directory.GetDirectories(root)
                    .Where(d => File.Exists(Path.Combine(d, FeedbackTicket.MarkdownFile)) && !File.Exists(Path.Combine(d, FiledFile)))
                    .OrderBy(d => Path.GetFileName(d), StringComparer.Ordinal).ToList();

        /// <summary>Files every pending ticket under <paramref name="root"/>. Blocks for as long as the uploads take: run it off the main thread.</summary>
        public static List<Outcome> FilePending(string root, string repoRoot, Action<string> progress = null)
        {
            List<string> pending = Pending(root);
            if (pending.Count == 0) return new List<Outcome>();
            progress?.Invoke("Checking the GitHub CLI…");
            string gh = FindGh(out string problem);
            if (gh == null) return pending.Select(folder => new Outcome { Folder = folder, Error = problem }).ToList();

            string repo = Repo(repoRoot);
            progress?.Invoke($"Preparing {repo}…");
            if (!Shell.Run(gh, repoRoot, new[] { "release", "view", Release, "--repo", repo }, 60000).Ok)
            {
                Shell.Result created = Shell.Run(gh, repoRoot, new[]
                {
                    "release", "create", Release, "--repo", repo, "--prerelease", "--title", "Feedback attachments",
                    "--notes", "Screenshots and game state attached to issues filed from the in-game feedback tool (Assets/_Game/Feedback).",
                }, 60000);
                if (!created.Ok)
                    return pending.Select(folder => new Outcome { Folder = folder, Error = "couldn't make the attachments release: " + created.Error }).ToList();
            }
            // Fails harmlessly when the label is already there.
            Shell.Run(gh, repoRoot, new[] { "label", "create", Label, "--repo", repo, "--color", "FBCA04", "--description", "Filed from the in-game feedback tool" }, 60000);

            return pending.Select(folder => FileTicket(gh, repo, repoRoot, folder, progress)).ToList();
        }

        static Outcome FileTicket(string gh, string repo, string repoRoot, string folder, Action<string> progress)
        {
            string name = Path.GetFileName(folder);
            var outcome = new Outcome { Folder = folder };
            try
            {
                JObject ticket = (JObject)JObject.Parse(System.IO.File.ReadAllText(Path.Combine(folder, FeedbackTicket.ContextFile)))["ticket"] ?? new JObject();
                string title = FeedbackTicket.TitleFor((string)ticket["category"], (string)ticket["title"], (string)ticket["description"]);

                // Each file goes up under a name of its own (the ticket's folder and when it was filed), so tickets
                // with the same number from two machines don't overwrite each other's.
                var urls = new Dictionary<string, string>();
                string staging = Path.Combine(Path.GetTempPath(), "game-feedback");
                Directory.CreateDirectory(staging);
                string stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
                foreach (string path in Directory.GetFiles(folder))
                {
                    string file = Path.GetFileName(path);
                    if (file == FeedbackTicket.MarkdownFile || file == FiledFile) continue;
                    progress?.Invoke($"Uploading {file}…");
                    string asset = $"{name}-{stamp}-{file}";
                    string staged = Path.Combine(staging, asset);
                    System.IO.File.Copy(path, staged, true);
                    Shell.Result upload = Shell.Run(gh, repoRoot, new[] { "release", "upload", Release, staged, "--repo", repo, "--clobber" }, 300000);
                    System.IO.File.Delete(staged);
                    if (!upload.Ok)
                    {
                        outcome.Error = $"couldn't upload {file}: {upload.Error}";
                        return outcome;
                    }
                    urls[file] = $"https://github.com/{repo}/releases/download/{Release}/{Uri.EscapeDataString(asset)}";
                }

                progress?.Invoke("Opening the issue…");
                string body = Path.Combine(staging, name + "-body.md");
                System.IO.File.WriteAllText(body, FeedbackTicket.WithLinks(System.IO.File.ReadAllText(Path.Combine(folder, FeedbackTicket.MarkdownFile)), urls));
                var args = new List<string> { "issue", "create", "--repo", repo, "--title", title, "--body-file", body, "--label", Label };
                Shell.Result issue = Shell.Run(gh, repoRoot, args, 120000);
                if (!issue.Ok && issue.Error.IndexOf("label", StringComparison.OrdinalIgnoreCase) >= 0)
                    issue = Shell.Run(gh, repoRoot, args.Take(args.Count - 2), 120000); // no right to make labels: file it without
                System.IO.File.Delete(body);
                string url = issue.Output?.Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.StartsWith("https://"));
                if (!issue.Ok || url == null)
                {
                    outcome.Error = "couldn't open the issue: " + (issue.Error.Length > 0 ? issue.Error : issue.Output);
                    return outcome;
                }

                outcome.Url = url;
                var filed = new JObject
                {
                    ["url"] = url, ["repo"] = repo, ["filed"] = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:sszzz"),
                    ["attachments"] = JObject.FromObject(urls),
                };
                if (int.TryParse(url.Substring(url.LastIndexOf('/') + 1), out int number)) filed["number"] = number;
                System.IO.File.WriteAllText(Path.Combine(folder, FiledFile), filed.ToString(Formatting.Indented));
                return outcome;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is JsonException)
            {
                outcome.Error = e.Message;
                return outcome;
            }
        }

        /// <summary>The gh command that runs and is signed in, or null and why not.</summary>
        static string FindGh(out string problem)
        {
            foreach (string exe in new[] { "gh", @"C:\Program Files\GitHub CLI\gh.exe", "/opt/homebrew/bin/gh", "/usr/local/bin/gh" })
            {
                Shell.Result status = Shell.Run(exe, null, new[] { "auth", "status" }, 30000);
                if (status.Code == -1) continue; // not installed there
                problem = status.Ok ? null : "the GitHub CLI isn't signed in (run gh auth login)";
                return status.Ok ? exe : null;
            }
            problem = "the GitHub CLI (gh) isn't installed (https://cli.github.com)";
            return null;
        }
    }
}
