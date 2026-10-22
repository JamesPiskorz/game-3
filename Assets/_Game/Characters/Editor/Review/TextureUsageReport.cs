using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Game.Characters.Content;
using Game.Characters.ContentAuthoring;
using Game.Characters.Dna;
using Game.Characters.Generation;
using UnityEditor;
using UnityEngine;

namespace Game.Characters.Review
{
    /// <summary>
    /// How often the town wears each fabric and print (decision 0008): 3000 generated people's every outfit, counted,
    /// written to Docs/review/texture-usage.md with the rarest first, so a fabric or print nobody wears can be given to
    /// more garments or a higher weight. Uses the generated content.
    /// Headless: unity run . -- -executeMethod Game.Characters.Review.TextureUsageReport.Write
    /// </summary>
    public static class TextureUsageReport
    {
        const int People = 3000;

        [MenuItem("Game/Characters/Review/Write Texture Usage Report")]
        public static void Write()
        {
            var content = GeneratedContent.Load();
            if (content == null)
            {
                Debug.LogError("[Review] No generated content: Game > Characters > Content > Write Generated Content.");
                return;
            }
            ContentCatalog catalog = content.LoadCatalog();
            var generator = new CharacterGenerator(catalog);
            var fabrics = catalog.Fabrics.ToDictionary(f => f.Id, f => 0);
            var prints = catalog.Patterns.ToDictionary(p => p.Id, p => 0);
            int items = 0, printed = 0;
            for (ulong seed = 1; seed <= People; seed++)
                foreach (OutfitDna outfit in generator.Generate(seed * 2654435761UL, GenerationRequest.Any).Outfits)
                    foreach (ItemDna item in outfit.Items)
                    {
                        PartEntry part = catalog.Part(item.ItemId);
                        FabricEntry fabric = catalog.FabricOf(part, item.FabricId);
                        if (fabric == null) continue;
                        items++;
                        fabrics[fabric.Id]++;
                        if (item.PatternId == null) continue;
                        printed++;
                        prints[item.PatternId]++;
                    }

            var md = new StringBuilder();
            md.AppendLine("# Texture usage");
            md.AppendLine();
            md.AppendLine($"_{People} generated people, every outfit: {items} items, {printed} printed ({100f * printed / items:0}%). " +
                          "Written by `TextureUsageReport`; rarest first._");
            md.AppendLine();
            md.AppendLine($"## Fabrics ({fabrics.Count})");
            md.AppendLine();
            md.AppendLine("| Fabric | Items | Share |");
            md.AppendLine("|---|---|---|");
            foreach (var pair in fabrics.OrderBy(p => p.Value))
                md.AppendLine($"| `{pair.Key}` | {pair.Value} | {100f * pair.Value / items:0.00}% |");
            md.AppendLine();
            md.AppendLine($"## Prints ({prints.Count})");
            md.AppendLine();
            md.AppendLine("| Print | Items | Share of printed |");
            md.AppendLine("|---|---|---|");
            foreach (var pair in prints.OrderBy(p => p.Value))
                md.AppendLine($"| `{pair.Key}` | {pair.Value} | {100f * pair.Value / printed:0.00}% |");
            string path = GeneratedLineup.Folder + "/texture-usage.md";
            File.WriteAllText(path, md.ToString());
            Debug.Log($"[Review] Wrote {path}: {fabrics.Count(p => p.Value == 0)} fabrics and {prints.Count(p => p.Value == 0)} prints nobody wears.");
        }
    }
}
