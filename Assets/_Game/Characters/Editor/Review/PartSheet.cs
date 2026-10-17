using System.Collections.Generic;
using System.Linq;
using Game.Characters.Building;
using Game.Characters.Content;
using Game.Characters.ContentAuthoring;
using Game.Characters.Dna;
using Game.Characters.Generation;
using Game.Characters.Modular;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Game.Characters.Review
{
    /// <summary>
    /// Every part in the catalog on the same model, front and back, in the same test colours
    /// (Docs/review/parts.png). Judges each piece on its own, away from the generator's choices: shape, region
    /// masks, clipping, how it sits on the body. Run it after adding or changing a part.
    /// Headless: unity run . -- -executeMethod Game.Characters.Review.PartSheet.Render
    /// </summary>
    public static class PartSheet
    {
        static readonly HsvColor[] TestColours =
        {
            new HsvColor(0.6f, 0.45f, 0.78f), new HsvColor(0f, 0f, 0.22f), new HsvColor(0.11f, 0.18f, 0.96f), new HsvColor(0.95f, 0.55f, 0.85f),
        };

        [MenuItem("Game/Characters/Review/Render Part Sheet")]
        public static void Render()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            CharacterContent content = StandInContent.Load();
            ContentCatalog catalog = content.LoadCatalog();
            var builder = new CharacterBuilder(content);
            CharacterDna model = new CharacterGenerator(catalog).Generate(11, new GenerationRequest { Archetype = "casual", FemininityMin = 0.45f, FemininityMax = 0.55f });

            List<PartEntry> parts = catalog.Parts.OrderBy(p => p.Slot, System.StringComparer.Ordinal).ThenBy(p => p.Id, System.StringComparer.Ordinal).ToList();
            Vector3[] positions = parts.Select((_, i) => new Vector3(i * 3f, 0f, 0f)).ToArray();
            LightingRig.Day.Apply(positions);
            Camera camera = Snapshot.CreateCamera(LightingRig.Day.Background);

            var shots = new List<Texture2D>();
            for (int i = 0; i < parts.Count; i++)
            {
                CharacterDna dna = Wearing(model, parts[i], catalog);
                ModularCharacter character = builder.Build(dna, "casual");
                character.transform.position = positions[i];
                Poses.Relaxed(character.transform);
                Vector3 origin = positions[i];
                bool head = parts[i].Slot.StartsWith("hair") || parts[i].Slot == Slots.Hat || parts[i].Slot == Slots.Glasses || parts[i].Slot == Slots.Earrings || parts[i].Slot == Slots.HairAccessory || Makeup.IsMark(parts[i].Slot);
                Vector3 target = origin + (head ? new Vector3(0f, 1.62f, 0f) : new Vector3(0f, 0.93f, 0f));
                float distance = head ? 0.9f : 3.7f;
                shots.Add(Snapshot.Shoot(camera, target + new Vector3(0.15f * distance, 0.05f, distance), target, 220, 300));
                shots.Add(Snapshot.Shoot(camera, target + new Vector3(-0.55f * distance, 0.05f, -0.85f * distance), target, 220, 300));
            }

            Snapshot.Save(Snapshot.Grid(shots, 12, Color.black), GeneratedLineup.Folder + "/parts.png");
            Debug.Log($"[Review] Part sheet: {parts.Count} parts, front and back, in slot order: " + string.Join(", ", parts.Select(p => p.Id)));
        }

        /// <summary>
        /// Every back hairpiece over the bulkiest layering (a hoodie under the coat), chest up, front and three-quarter
        /// (Docs/review/hair-over-coat.png): long hair and plaits worn in front must sit over the coat, not in it.
        /// </summary>
        [MenuItem("Game/Characters/Review/Render Hair Over Coat Sheet")]
        public static void RenderHairOverCoat()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            CharacterContent content = StandInContent.Load();
            ContentCatalog catalog = content.LoadCatalog();
            var builder = new CharacterBuilder(content);
            CharacterDna model = new CharacterGenerator(catalog).Generate(11, new GenerationRequest { Archetype = "casual", FemininityMin = 0.45f, FemininityMax = 0.55f });
            CharacterDna coated = Wearing(Wearing(model, catalog.Part("cloth.top.hoodie_01"), catalog), catalog.Part("cloth.outer.coat_01"), catalog);

            List<PartEntry> backs = catalog.PartsInSlot(Slots.HairBack).OrderBy(p => p.Id, System.StringComparer.Ordinal).ToList();
            Vector3[] positions = backs.Select((_, i) => new Vector3(i * 3f, 0f, 0f)).ToArray();
            LightingRig.Day.Apply(positions);
            Camera camera = Snapshot.CreateCamera(LightingRig.Day.Background);

            var shots = new List<Texture2D>();
            for (int i = 0; i < backs.Count; i++)
            {
                ModularCharacter character = builder.Build(Wearing(coated, backs[i], catalog), "casual");
                character.transform.position = positions[i];
                Poses.Relaxed(character.transform);
                Vector3 target = positions[i] + new Vector3(0f, 1.4f, 0f);
                shots.Add(Snapshot.Shoot(camera, target + new Vector3(0.2f, 0.05f, 2.2f), target, 220, 300));
                shots.Add(Snapshot.Shoot(camera, target + new Vector3(1.9f, 0.05f, 1.1f), target, 220, 300));
            }

            Snapshot.Save(Snapshot.Grid(shots, 12, Color.black), GeneratedLineup.Folder + "/hair-over-coat.png");
            Debug.Log($"[Review] Hair over coat: {backs.Count} back pieces: " + string.Join(", ", backs.Select(p => p.Id)));
        }

        /// <summary>The model wearing <paramref name="part"/> in test colours, with whatever it needs to be a whole outfit.</summary>
        static CharacterDna Wearing(CharacterDna model, PartEntry part, ContentCatalog catalog)
        {
            CharacterDna dna = DnaSerializer.FromJson(DnaSerializer.ToJson(model));
            OutfitDna outfit = dna.Outfits.First(o => o.Context == "casual");
            string SlotOf(ItemDna item) => catalog.Part(item.ItemId)?.Slot;
            ItemDna Item(string id) => new ItemDna { ItemId = id, RegionColors = TestColours.Take(catalog.Part(id).Regions).ToList() };

            if (part.Slot.StartsWith("hair"))
            {
                dna.Hair.PieceIds = dna.Hair.PieceIds.Where(id => catalog.Part(id)?.Slot != part.Slot).Append(part.Id).ToList();
                return dna;
            }

            // Hats need hair that takes one.
            PartEntry hair = part.RequiresTags.Count == 0 ? null : catalog.Parts.FirstOrDefault(p => p.Slot == Slots.HairBack && part.RequiresTags.All(p.Tags.Contains));
            if (hair != null) dna.Hair.PieceIds = new List<string> { hair.Id };

            outfit.Items.RemoveAll(item =>
            {
                PartEntry other = catalog.Part(item.ItemId);
                return other.Slot == part.Slot || part.BlocksSlots.Contains(other.Slot) || other.BlocksSlots.Contains(part.Slot);
            });
            outfit.Items.Add(Item(part.Id));
            // Legwear needs legs that show: swap the bottom for one that shows them.
            bool Provided(string tag) => dna.Hair.PieceIds.Concat(outfit.Items.Select(i => i.ItemId)).Any(id => catalog.Part(id)?.Tags.Contains(tag) == true);
            PartEntry showing = part.RequiresTags.All(Provided) ? null : catalog.PartsInSlot(Slots.Bottom).FirstOrDefault(b => part.RequiresTags.All(b.Tags.Contains));
            if (showing != null)
            {
                outfit.Items.RemoveAll(i => SlotOf(i) == Slots.Bottom || SlotOf(i) == Slots.OnePiece);
                outfit.Items.Add(Item(showing.Id));
            }
            bool onePiece = outfit.Items.Any(i => SlotOf(i) == Slots.OnePiece);
            if (!onePiece && outfit.Items.All(i => SlotOf(i) != Slots.Top)) outfit.Items.Add(Item("cloth.top.tshirt_01"));
            if (!onePiece && outfit.Items.All(i => SlotOf(i) != Slots.Bottom)) outfit.Items.Add(Item("cloth.bottom.pants_01"));
            if (outfit.Items.All(i => SlotOf(i) != Slots.Shoes)) outfit.Items.Add(Item("cloth.shoes.sneakers_01"));
            return dna;
        }
    }
}
