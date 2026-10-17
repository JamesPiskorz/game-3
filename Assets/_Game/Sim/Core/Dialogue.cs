using System.Collections.Generic;
using System.Linq;
using System.Text;
using Game.Characters.Rng;
using Game.Sim.Relationships;

namespace Game.Sim
{
    /// <summary>
    /// Picks a line for a reaction key ("talk.love") that fits the resident's tone and how close they are to the
    /// player, avoiding the line they said last time, and fills in its {tokens}.
    /// </summary>
    public sealed class Dialogue
    {
        readonly SimCatalog _catalog;

        public Dialogue(SimCatalog catalog) => _catalog = catalog;

        public bool Has(string key) => _catalog.Dialogue.TryGetValue(key, out List<DialogueLine> lines) && lines.Count > 0;

        /// <param name="avoid">The template used last time for this key, skipped when there's an alternative.</param>
        public string Line(string key, string tone, Stage stage, Pcg32 rng, IReadOnlyDictionary<string, string> tokens = null, string avoid = null) =>
            Fill(Template(key, tone, stage, rng, avoid), tokens);

        /// <summary>The unfilled line, so callers can remember which one was used.</summary>
        public string Template(string key, string tone, Stage stage, Pcg32 rng, string avoid = null)
        {
            if (!_catalog.Dialogue.TryGetValue(key, out List<DialogueLine> lines) || lines.Count == 0) return "...";

            List<DialogueLine> fitting = lines.Where(l => l.Fits(tone, stage)).ToList();
            if (fitting.Count == 0) fitting = lines.Where(l => l.MinStage == null && l.MaxStage == null).ToList();
            if (fitting.Count == 0) fitting = lines;
            if (fitting.Count > 1 && avoid != null) fitting = fitting.Where(l => l.Text != avoid).DefaultIfEmpty(fitting[0]).ToList();
            return rng.Pick(fitting).Text;
        }

        public static string Fill(string text, IReadOnlyDictionary<string, string> tokens)
        {
            var result = new StringBuilder(text);
            if (tokens != null)
                foreach (KeyValuePair<string, string> token in tokens)
                    result.Replace("{" + token.Key + "}", token.Value ?? "");
            if (result.Length > 0) result[0] = char.ToUpperInvariant(result[0]);
            return result.ToString();
        }
    }
}
