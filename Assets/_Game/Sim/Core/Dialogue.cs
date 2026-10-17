using System.Collections.Generic;
using System.Linq;
using System.Text;
using Game.Characters.Rng;

namespace Game.Sim
{
    /// <summary>
    /// Picks a line for a reaction key ("talk.love") in the resident's tone, falling back to lines written for any
    /// tone, and fills in its {tokens}.
    /// </summary>
    public sealed class Dialogue
    {
        readonly SimCatalog _catalog;

        public Dialogue(SimCatalog catalog) => _catalog = catalog;

        public string Line(string key, string tone, Pcg32 rng, IReadOnlyDictionary<string, string> tokens = null)
        {
            if (!_catalog.Dialogue.TryGetValue(key, out List<DialogueLine> lines) || lines.Count == 0) return "...";

            List<DialogueLine> fitting = lines.Where(l => l.Tone == tone || l.Tone == Tones.Any).ToList();
            DialogueLine line = rng.Pick(fitting.Count > 0 ? fitting : lines);
            return Fill(line.Text, tokens);
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
