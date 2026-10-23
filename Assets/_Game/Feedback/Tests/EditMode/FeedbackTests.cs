using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Game.Feedback.Tests
{
    /// <summary>Feedback tickets: their folders and issue text, filing's helpers, and how materials are described.</summary>
    public class FeedbackTests
    {
        string _root;

        [SetUp]
        public void MakeFolder() => _root = Path.Combine(Path.GetTempPath(), "feedback-tests-" + System.Guid.NewGuid().ToString("N"));

        [TearDown]
        public void RemoveFolder()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }

        static FeedbackTicket Ticket() => new FeedbackTicket
        {
            Title = "Hoodie clips through the arm!",
            Description = "Her sleeve shows skin at the elbow when she waves.",
            Category = "Looks wrong",
            Screenshot = new byte[] { 137, 80, 78, 71 },
            Summary = { ("Game time", "Sunday (day 7) 08:00"), ("Place", "Cafe") },
            OnScreen = { "**Mia** · seed `1a2b` · cute · casual outfit: top.hoodie in velour" },
            Attachments = { ["town-save.json"] = "{}" },
        };

        [Test]
        public void SlugsAreShortAndFileSafe()
        {
            Assert.That(FeedbackTicket.Slug("Hoodie clips through the arm!"), Is.EqualTo("hoodie-clips-through-the-arm"));
            Assert.That(FeedbackTicket.Slug("  "), Is.EqualTo("feedback"));
            string slug = FeedbackTicket.Slug(string.Join(" ", Enumerable.Repeat("everything is wrong with this jacket", 5)));
            Assert.That(slug.Length, Is.LessThanOrEqualTo(48));
            Assert.That(slug, Does.Match("^[a-z0-9]+(-[a-z0-9]+)*$"));
            Assert.That(FeedbackTicket.Slug("Ünïcode & émoji 🎉 / slashes"), Does.Match("^[a-z0-9]+(-[a-z0-9]+)*$"));
        }

        [Test]
        public void TicketsAreNumberedAfterTheLastOne()
        {
            Assert.That(FeedbackTicket.NextNumber(_root), Is.EqualTo(1));
            Directory.CreateDirectory(Path.Combine(_root, "0001-a"));
            Directory.CreateDirectory(Path.Combine(_root, "0007-b"));
            Directory.CreateDirectory(Path.Combine(_root, "notes"));
            Assert.That(FeedbackTicket.NextNumber(_root), Is.EqualTo(8));
        }

        [Test]
        public void SavingWritesTheTicketsFolder()
        {
            string folder = Ticket().Save(_root);
            Assert.That(Path.GetFileName(folder), Is.EqualTo("0001-hoodie-clips-through-the-arm"));
            foreach (string file in new[] { "ticket.md", "context.json", "screenshot.png", "town-save.json" })
                Assert.That(File.Exists(Path.Combine(folder, file)), file);

            JObject context = JObject.Parse(File.ReadAllText(Path.Combine(folder, "context.json")));
            Assert.That((string)context["ticket"]["title"], Is.EqualTo("Hoodie clips through the arm!"));
            Assert.That((string)context["ticket"]["category"], Is.EqualTo("Looks wrong"));

            string markdown = File.ReadAllText(Path.Combine(folder, "ticket.md"));
            Assert.That(markdown, Does.StartWith("Her sleeve shows skin"));
            Assert.That(markdown, Does.Contain("![Screenshot](screenshot.png)"));
            Assert.That(markdown, Does.Contain("| Place | Cafe |"));
            Assert.That(markdown, Does.Contain("top.hoodie in velour"));
            Assert.That(markdown, Does.Contain("[game state](context.json)"));
            Assert.That(markdown, Does.Contain("[town-save.json](town-save.json)"));

            Assert.That(Path.GetFileName(Ticket().Save(_root)), Does.StartWith("0002-"), "The next ticket takes the next number.");
        }

        [Test]
        public void FilingPointsTheLinksAtTheUploads()
        {
            string markdown = Ticket().ToMarkdown();
            string filed = FeedbackTicket.WithLinks(markdown, new Dictionary<string, string>
            {
                ["screenshot.png"] = "https://example.com/s.png", ["context.json"] = "https://example.com/c.json",
            });
            Assert.That(filed, Does.Contain("![Screenshot](https://example.com/s.png)"));
            Assert.That(filed, Does.Contain("[game state](https://example.com/c.json)"));
            Assert.That(filed, Does.Not.Contain("](screenshot.png)"));
        }

        [Test]
        public void TheIssueStaysUnderGitHubsLimit()
        {
            FeedbackTicket ticket = Ticket();
            for (int i = 0; i < 2000; i++) ticket.OnScreen.Add($"**Person {i}** " + new string('x', 200));
            ticket.Context["logs"] = new JArray(Enumerable.Range(0, 200).Select(i => new JObject
            {
                ["time"] = "12:00:00.000", ["type"] = "Error", ["message"] = "Something broke " + i, ["stack"] = string.Join("\n", Enumerable.Repeat("at Somewhere.Deep()", 40)),
            }));
            string markdown = ticket.ToMarkdown();
            Assert.That(markdown.Length, Is.LessThanOrEqualTo(FeedbackTicket.MaxBody));
            Assert.That(markdown, Does.Contain("more (context.json)"));
            Assert.That(markdown, Does.Contain("Recent warnings and errors (200)"));
        }

        [Test]
        public void IssueTitlesSayWhatKindOfFeedback()
        {
            Assert.That(FeedbackTicket.TitleFor("Looks wrong", "", "The jeans are see-through\nwhen she sits"), Is.EqualTo("[Looks wrong] The jeans are see-through"));
            Assert.That(FeedbackTicket.TitleFor("Idea", " A dance floor ", ""), Is.EqualTo("[Idea] A dance floor"));
            Assert.That(FeedbackTicket.TitleFor(null, "", ""), Is.EqualTo("[Bug] Feedback"));
        }

        [TestCase("https://github.com/JamesPiskorz/game-3.git", "JamesPiskorz/game-3")]
        [TestCase("https://github.com/JamesPiskorz/game-3", "JamesPiskorz/game-3")]
        [TestCase("git@github.com:JamesPiskorz/game-3.git", "JamesPiskorz/game-3")]
        [TestCase("https://gitlab.com/someone/thing.git", null)]
        public void TheRepositoryComesFromTheRemote(string remote, string repo) =>
            Assert.That(GitHubIssues.RepoFromRemote(remote), Is.EqualTo(repo));

        [Test]
        public void TicketsWithoutAnIssueAreStillToBeFiled()
        {
            foreach (string name in new[] { "0001-filed", "0002-waiting", "0003-half-written" }) Directory.CreateDirectory(Path.Combine(_root, name));
            File.WriteAllText(Path.Combine(_root, "0001-filed", "ticket.md"), "");
            File.WriteAllText(Path.Combine(_root, "0001-filed", GitHubIssues.FiledFile), "{}");
            File.WriteAllText(Path.Combine(_root, "0002-waiting", "ticket.md"), "");
            Assert.That(GitHubIssues.Pending(_root).Select(Path.GetFileName), Is.EqualTo(new[] { "0002-waiting" }));
        }

        [Test]
        public void ArgumentsAreQuotedForTheCommandLine()
        {
            Assert.That(Shell.Quote("plain"), Is.EqualTo("plain"));
            Assert.That(Shell.Quote(""), Is.EqualTo("\"\""));
            Assert.That(Shell.Quote("two words"), Is.EqualTo("\"two words\""));
            Assert.That(Shell.Quote("say \"hi\""), Is.EqualTo("\"say \\\"hi\\\"\""));
            Assert.That(Shell.Quote(@"C:\a folder\"), Is.EqualTo("\"C:\\a folder\\\\\""));
            Assert.That(Shell.Quote(@"a\\""b c"), Is.EqualTo("\"a\\\\\\\\\\\"b c\""));
        }

        [Test]
        public void AMaterialIsDescribedByEveryValueItsDrawnWith()
        {
            Shader shader = Shader.Find("Game/Characters/Toon");
            Assert.That(shader, Is.Not.Null);
            var material = new Material(shader) { name = "Test toon" };
            try
            {
                material.SetFloat("_Gloss", 0.5f);
                material.SetColor("_BaseColor", new Color(1f, 0.5f, 0.25f, 1f));
                JObject described = FeedbackContext.DescribeMaterial(material);
                Assert.That((string)described["shader"], Is.EqualTo("Game/Characters/Toon"));
                Assert.That((double)described["properties"]["_Gloss"], Is.EqualTo(0.5).Within(1e-4));
                Assert.That(described["properties"]["_BaseColor"].Select(v => (double)v), Is.EqualTo(new[] { 1.0, 0.5, 0.25, 1.0 }).Within(1e-4));
                Assert.That(((JObject)described["properties"]).Count, Is.GreaterThanOrEqualTo(shader.GetPropertyCount()));
            }
            finally
            {
                Object.DestroyImmediate(material);
            }
        }
    }
}
