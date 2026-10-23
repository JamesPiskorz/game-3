using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Game.Feedback
{
    /// <summary>
    /// Stamps player builds with the commit they're built from, so a ticket from a build says which code it ran: writes
    /// Resources/feedback-build.json for the length of the build (FeedbackContext reads it), then removes it.
    /// </summary>
    public sealed class FeedbackBuildInfo : IPreprocessBuildWithReport, IPostprocessBuildWithReport
    {
        const string Folder = "Assets/_Game/Feedback/Resources";
        static string AssetPath => $"{Folder}/{FeedbackContext.BuildInfoResource}.json";

        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            string root = Directory.GetParent(Application.dataPath).FullName;
            Shell.Result commit = Shell.Run("git", root, new[] { "rev-parse", "HEAD" }, 10000);
            Shell.Result branch = Shell.Run("git", root, new[] { "rev-parse", "--abbrev-ref", "HEAD" }, 10000);
            Shell.Result status = Shell.Run("git", root, new[] { "status", "--porcelain" }, 10000);
            var info = new JObject { ["built"] = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:sszzz") };
            if (commit.Ok) info["commit"] = commit.Output;
            if (branch.Ok) info["branch"] = branch.Output;
            if (status.Ok) info["dirty"] = status.Output.Length > 0;
            Directory.CreateDirectory(Folder);
            File.WriteAllText(AssetPath, info.ToString());
            AssetDatabase.ImportAsset(AssetPath);
        }

        public void OnPostprocessBuild(BuildReport report)
        {
            AssetDatabase.DeleteAsset(AssetPath);
            if (AssetDatabase.IsValidFolder(Folder) && !Directory.EnumerateFileSystemEntries(Folder).Any()) AssetDatabase.DeleteAsset(Folder);
        }
    }

    /// <summary>The tickets from the editor: show them, and file those still waiting (saved offline, or before gh was signed in).</summary>
    public static class FeedbackMenu
    {
        [MenuItem("Game/Feedback/Show Tickets")]
        static void ShowTickets()
        {
            Directory.CreateDirectory(FeedbackPaths.TicketsRoot);
            EditorUtility.RevealInFinder(FeedbackPaths.TicketsRoot);
        }

        /// <summary>Headless: unity run . -- -executeMethod Game.Feedback.FeedbackMenu.FileWaitingTickets</summary>
        [MenuItem("Game/Feedback/File Waiting Tickets on GitHub")]
        public static void FileWaitingTickets()
        {
            List<GitHubIssues.Outcome> outcomes;
            try
            {
                outcomes = GitHubIssues.FilePending(FeedbackPaths.TicketsRoot, FeedbackPaths.RepoRoot,
                    message => EditorUtility.DisplayProgressBar("Feedback", message, 0.5f));
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
            if (outcomes.Count == 0) Debug.Log("[Feedback] No tickets waiting.");
            foreach (GitHubIssues.Outcome outcome in outcomes)
                if (outcome.Filed) Debug.Log($"[Feedback] Filed {Path.GetFileName(outcome.Folder)} as {outcome.Url}");
                else Debug.LogWarning($"[Feedback] Didn't file {Path.GetFileName(outcome.Folder)}: {outcome.Error}");
        }
    }
}
