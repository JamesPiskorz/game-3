using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Game.Feedback
{
    /// <summary>
    /// One piece of feedback: what the player wrote, a screenshot and the game state at the moment they asked to give it.
    /// Saved as a folder of its own under feedback-tickets/ (<see cref="FolderName"/>: its number and title) holding
    /// ticket.md (the issue's text, linking the other files by name), context.json, screenshot.png and any attachments
    /// (the town save); then filed as a GitHub issue by <see cref="GitHubIssues"/>, which links the uploaded files instead.
    /// </summary>
    public sealed class FeedbackTicket
    {
        public const string MarkdownFile = "ticket.md", ContextFile = "context.json", ScreenshotFile = "screenshot.png";
        public static readonly string[] Categories = { "Bug", "Looks wrong", "Idea", "Other" };

        /// <summary>GitHub refuses issue bodies over 65,536 characters; keep well clear.</summary>
        public const int MaxBody = 60000;

        public int Number;
        public string Title = "";
        public string Description = "";
        public string Category = Categories[0];
        public DateTime Created = DateTime.Now;

        /// <summary>Everything known about the game state (context.json).</summary>
        public JObject Context = new JObject();

        /// <summary>The screen as a PNG, or null.</summary>
        public byte[] Screenshot;

        /// <summary>Rows for the issue's summary table: the scene, the game time, the place, the build.</summary>
        public List<(string Label, string Value)> Summary = new List<(string, string)>();

        /// <summary>A line per character on screen: who, and what they wear in which fabric and print.</summary>
        public List<string> OnScreen = new List<string>();

        /// <summary>Other files to attach, by name (town-save.json).</summary>
        public Dictionary<string, string> Attachments = new Dictionary<string, string>();

        public string FolderName => $"{Number:0000}-{Slug(Title.Length > 0 ? Title : Description)}";

        public string IssueTitle => TitleFor(Category, Title, Description);

        /// <summary>"[Bug] Hoodie clips through the arm": the category, then the title or the description's first line.</summary>
        public static string TitleFor(string category, string title, string description) =>
            $"[{(string.IsNullOrWhiteSpace(category) ? Categories[0] : category)}] {(string.IsNullOrWhiteSpace(title) ? FirstLine(description, 70) : title.Trim())}";

        /// <summary>Writes the ticket's folder under <paramref name="root"/>, numbering it after the last one, and returns its path.</summary>
        public string Save(string root)
        {
            Directory.CreateDirectory(root);
            if (Number <= 0) Number = NextNumber(root);
            while (Directory.Exists(Path.Combine(root, FolderName))) Number++;
            string folder = Path.Combine(root, FolderName);
            Directory.CreateDirectory(folder);

            Context["ticket"] = new JObject
            {
                ["number"] = Number, ["title"] = Title, ["category"] = Category, ["description"] = Description,
                ["created"] = Created.ToString("yyyy-MM-ddTHH:mm:sszzz"),
            };
            File.WriteAllText(Path.Combine(folder, ContextFile), Context.ToString(Formatting.Indented));
            if (Screenshot != null) File.WriteAllBytes(Path.Combine(folder, ScreenshotFile), Screenshot);
            foreach (KeyValuePair<string, string> attachment in Attachments)
                File.WriteAllText(Path.Combine(folder, SafeFileName(attachment.Key)), attachment.Value);
            File.WriteAllText(Path.Combine(folder, MarkdownFile), ToMarkdown());
            return folder;
        }

        /// <summary>One more than the highest numbered ticket folder in <paramref name="root"/>.</summary>
        public static int NextNumber(string root)
        {
            if (!Directory.Exists(root)) return 1;
            int highest = Directory.GetDirectories(root)
                .Select(path => Regex.Match(Path.GetFileName(path), @"^(\d+)-"))
                .Where(m => m.Success)
                .Select(m => int.Parse(m.Groups[1].Value))
                .DefaultIfEmpty(0).Max();
            return highest + 1;
        }

        /// <summary>A short, file-safe version of a title: "Hoodie clips through the arm!" → "hoodie-clips-through-the-arm".</summary>
        public static string Slug(string text, int max = 48)
        {
            string slug = Regex.Replace((text ?? "").ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
            if (slug.Length > max)
            {
                slug = slug.Substring(0, max);
                int cut = slug.LastIndexOf('-');
                if (cut > max / 2) slug = slug.Substring(0, cut);
            }
            return slug.Length > 0 ? slug : "feedback";
        }

        /// <summary>
        /// The issue's text: what the player wrote, the screenshot, a summary of the state and who's on screen, recent
        /// warnings and errors, and links to the attached files, all by file name (<see cref="WithLinks"/> points them at
        /// uploads). Long lists are cut to stay under <see cref="MaxBody"/>.
        /// </summary>
        public string ToMarkdown()
        {
            string body = Compose(OnScreen.Count, 15);
            for (int characters = Math.Min(OnScreen.Count, 30), logs = 8; body.Length > MaxBody && (characters > 0 || logs > 0); )
            {
                body = Compose(characters, logs);
                characters /= 2;
                logs /= 2;
            }
            return body.Length <= MaxBody ? body : body.Substring(0, MaxBody - 40) + "\n\n_(cut short: see context.json)_\n";
        }

        string Compose(int characters, int logs)
        {
            var md = new StringBuilder();
            md.AppendLine(string.IsNullOrWhiteSpace(Description) ? "_No description._" : Description.Trim());
            md.AppendLine();
            if (Screenshot != null)
            {
                md.AppendLine($"![Screenshot]({ScreenshotFile})");
                md.AppendLine();
            }

            md.AppendLine("| | |");
            md.AppendLine("|---|---|");
            md.AppendLine($"| Category | {Cell(Category)} |");
            foreach ((string label, string value) in Summary)
                md.AppendLine($"| {Cell(label)} | {Cell(value)} |");
            md.AppendLine();

            if (OnScreen.Count > 0)
            {
                md.AppendLine("### On screen");
                foreach (string line in OnScreen.Take(characters)) md.AppendLine("- " + line);
                if (OnScreen.Count > characters) md.AppendLine($"- _…and {OnScreen.Count - characters} more (context.json)_");
                md.AppendLine();
            }

            List<JToken> problems = (Context["logs"] as JArray)?.Where(e => (string)e["type"] != "Log").ToList() ?? new List<JToken>();
            if (problems.Count > 0 && logs > 0)
            {
                md.AppendLine($"<details><summary>Recent warnings and errors ({problems.Count})</summary>");
                md.AppendLine();
                md.AppendLine("```");
                foreach (JToken entry in problems.Skip(Math.Max(0, problems.Count - logs)))
                {
                    md.AppendLine($"[{entry["time"]}] {entry["type"]}: {entry["message"]}");
                    string stack = (string)entry["stack"];
                    if (!string.IsNullOrEmpty(stack))
                        foreach (string frame in stack.Split('\n').Where(l => l.Trim().Length > 0).Take(6)) md.AppendLine("    " + frame.Trim());
                }
                md.AppendLine("```");
                md.AppendLine("</details>");
                md.AppendLine();
            }

            if (Context["system"] is JObject system)
            {
                md.AppendLine("<details><summary>System</summary>");
                md.AppendLine();
                foreach (JProperty property in system.Properties()) md.AppendLine($"- {property.Name}: {property.Value}");
                md.AppendLine("</details>");
                md.AppendLine();
            }

            var files = new List<string> { $"[game state]({ContextFile})" };
            files.AddRange(Attachments.Keys.Select(name => $"[{SafeFileName(name)}]({SafeFileName(name)})"));
            md.AppendLine("**Attached:** " + string.Join(" · ", files));
            md.AppendLine();
            md.AppendLine($"<sub>Ticket {Number:0000} from the in-game feedback tool, {Created:yyyy-MM-dd HH:mm}. The game state holds the build, " +
                          "cameras, lights, every character's DNA and material values, the screen's text and the log.</sub>");
            return md.ToString();
        }

        /// <summary>The markdown with its links to files by name (<c>](screenshot.png)</c>) pointed at <paramref name="urls"/> instead.</summary>
        public static string WithLinks(string markdown, IReadOnlyDictionary<string, string> urls)
        {
            foreach (KeyValuePair<string, string> link in urls)
                markdown = markdown.Replace($"]({link.Key})", $"]({link.Value})");
            return markdown;
        }

        public static string SafeFileName(string name) => Regex.Replace(name ?? "attachment", @"[^\w.\-]+", "-");

        static string Cell(string text) => (text ?? "").Replace("|", "\\|").Replace("\r", "").Replace("\n", " ");

        static string FirstLine(string text, int max)
        {
            string line = (text ?? "").Trim().Split('\n')[0].Trim();
            if (line.Length == 0) return "Feedback";
            return line.Length <= max ? line : line.Substring(0, max - 1) + "…";
        }
    }
}
