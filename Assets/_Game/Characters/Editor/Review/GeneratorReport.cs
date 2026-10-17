using System.IO;
using Game.Characters.Content;
using Game.Characters.ContentAuthoring;
using Game.Characters.Generation;
using UnityEditor;
using UnityEngine;

namespace Game.Characters.Review
{
    /// <summary>
    /// Writes Docs/review/generator-stats.md: the generator's batch statistics over 10,000 characters
    /// (<see cref="GeneratorStats"/>). Re-run after every content drop, next to the lineup and quality sheet.
    /// Headless: -executeMethod Game.Characters.Review.GeneratorReport.Write
    /// </summary>
    public static class GeneratorReport
    {
        const string ReportPath = "Docs/review/generator-stats.md";

        [MenuItem("Game/Characters/Review/Write Generator Statistics")]
        public static void Write()
        {
            ContentCatalog catalog = StandInContent.Load().LoadCatalog();
            GeneratorStats stats = GeneratorStats.Measure(new CharacterGenerator(catalog), 10000);
            string header = $"_{System.DateTime.Now:yyyy-MM-dd HH:mm} · {SystemInfo.processorType.Trim()} · Unity {Application.unityVersion} (editor). " +
                            "Regenerate with **Game → Characters → Review → Write Generator Statistics**._";
            File.WriteAllText(ReportPath, stats.ToMarkdown(catalog, header).Replace("\r\n", "\n"));
            Debug.Log($"[Review] Generator statistics for {stats.Count} characters written to {ReportPath} ({stats.Violations} violations).");
        }
    }
}
