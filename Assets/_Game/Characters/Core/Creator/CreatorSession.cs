using System;
using System.Collections.Generic;
using System.Linq;
using Game.Characters.Content;
using Game.Characters.Dna;
using Game.Characters.Generation;
using Game.Characters.Rng;

namespace Game.Characters.Creator
{
    /// <summary>
    /// The state behind the character creator (PLAN.md §3.9): one DNA being edited, with undo/redo,
    /// randomize-with-locks and share codes. Pure C#, so every editing rule is tested without UI. Edits keep the DNA
    /// valid: sliders clamp to their safe ranges, and item swaps that would break slot or tag rules are refused with a
    /// reason rather than applied.
    /// </summary>
    public sealed class CreatorSession
    {
        const int MaxHistory = 100;

        readonly CharacterGenerator _generator;
        readonly ContentCatalog _catalog;
        readonly List<string> _undo = new List<string>();
        readonly List<string> _redo = new List<string>();
        readonly ulong _sessionSeed;
        ulong _rolls;
        string _mergeKey; // consecutive edits with the same key (one slider drag) share one undo step

        public CreatorSession(CharacterGenerator generator, CharacterDna start = null, ulong sessionSeed = 1)
        {
            _generator = generator;
            _catalog = generator.Catalog;
            _sessionSeed = sessionSeed;
            // A look made with other content (an old save) is brought up to date rather than refused.
            Dna = start != null ? DnaRepair.Repair(generator, Copy(start), out _) : generator.Generate(NextSeed());
            Context = Dna.Outfits.FirstOrDefault()?.Context ?? _catalog.Contexts[0].Id;
        }

        public CharacterDna Dna { get; private set; }
        public ContentCatalog Catalog => _catalog;

        /// <summary>Categories that Randomize leaves alone.</summary>
        public DnaCategories Locks { get; set; }

        /// <summary>The style Randomize aims for: an archetype ID, or null for any (by the catalog's weights).</summary>
        public string Style { get; set; }

        /// <summary>The outfit context being shown and edited.</summary>
        public string Context { get; private set; }

        /// <summary>Raised after every change to <see cref="Dna"/> or <see cref="Context"/>.</summary>
        public event Action Changed;

        public bool CanUndo => _undo.Count > 0;
        public bool CanRedo => _redo.Count > 0;

        public OutfitDna Outfit => Dna.Outfits.FirstOrDefault(o => o.Context == Context);

        /// <summary>
        /// The styles the current outfit reads as, strongest first (at most two). Each piece votes for the styles it's
        /// tagged with (a piece that suits everything says little, so its vote is split) and for the styles whose
        /// palette its main colour comes from. Residents who like those styles notice, so the creator can say who a
        /// look will appeal to.
        /// </summary>
        public List<string> StyleReads()
        {
            var scores = new Dictionary<string, float>();
            foreach (ItemDna item in Outfit?.Items ?? new List<ItemDna>())
            {
                PartEntry part = Catalog.Part(item.ItemId);
                if (part == null) continue;
                List<StyleArchetype> tagged = Catalog.Archetypes.Where(a => part.Tags.Contains(a.Id)).ToList();
                foreach (StyleArchetype archetype in Catalog.Archetypes)
                {
                    float score = tagged.Contains(archetype) ? 1f / tagged.Count : 0f;
                    ColourPalette palette = Catalog.Palette(archetype.OutfitPalette);
                    if (palette != null && item.RegionColors.Count > 0)
                    {
                        float nearest = palette.Colours.Where(c => !c.Neutral).Select(c => HsvColor.Distance(HsvColor.FromHex(c.Hex), item.RegionColors[0])).DefaultIfEmpty(1f).Min();
                        score += 0.6f * Math.Max(0f, 1f - nearest / 0.25f);
                    }
                    scores[archetype.Id] = (scores.TryGetValue(archetype.Id, out float total) ? total : 0f) + score;
                }
            }
            return scores.Where(s => s.Value > 0f).OrderByDescending(s => s.Value).ThenBy(s => s.Key, StringComparer.Ordinal)
                .Take(2).Select(s => s.Key).ToList();
        }

        // ---------------------------------------------------------------- whole-character operations

        /// <summary>
        /// Rerolls everything not locked. With nothing locked it's a whole new person, presentation included, in the
        /// chosen <see cref="Style"/> or any; with locks it keeps who they are and rerolls the rest (in the chosen
        /// style, if there is one).
        /// </summary>
        /// <summary>
        /// Characters Randomize could give right now (the chosen style, locked categories kept), to browse without
        /// changing anything; <see cref="Adopt"/> takes one.
        /// </summary>
        public List<CharacterDna> Variations(int count)
        {
            var variations = new List<CharacterDna>();
            for (int i = 0; i < count; i++)
            {
                ulong seed = NextSeed();
                var request = new GenerationRequest { Archetype = Style };
                DnaCategories free = DnaCategories.All & ~Locks;
                variations.Add(Locks == DnaCategories.None ? _generator.Generate(seed, request)
                    : free == DnaCategories.None ? Copy(Dna) : _generator.Reroll(Dna, free, seed, Style == null ? null : request));
            }
            return variations;
        }

        /// <summary>Makes <paramref name="candidate"/> (from <see cref="Variations"/>) the character, as one undo step.</summary>
        public void Adopt(CharacterDna candidate)
        {
            if (candidate == null || DnaValidator.Validate(_catalog, candidate).Count > 0) return;
            Commit(_ => Copy(candidate));
        }

        public void Randomize()
        {
            if (Locks == DnaCategories.None)
            {
                ulong fresh = NextSeed();
                var request = new GenerationRequest { Archetype = Style };
                Commit(_ => _generator.Generate(fresh, request));
                return;
            }
            Reroll(DnaCategories.All);
        }

        public void Reroll(DnaCategories categories)
        {
            DnaCategories free = categories & ~Locks;
            if (free == DnaCategories.None) return;
            ulong seed = NextSeed();
            GenerationRequest request = Style == null ? null : new GenerationRequest { Archetype = Style };
            Commit(dna => dna == null ? null : _generator.Reroll(dna, free, seed, request));
        }

        /// <summary>
        /// A brother or sister of the current character: a new person who shares the family look (see
        /// <see cref="FamilyResemblance"/>). They're someone else, so locks don't hold; undo brings the original back.
        /// </summary>
        public void Sibling()
        {
            ulong seed = NextSeed();
            Commit(dna => dna == null ? null : _generator.GenerateRelative(dna, seed));
        }

        public void SetContext(string context)
        {
            if (_catalog.Context(context) == null) throw new ArgumentException($"Unknown context '{context}'.");
            Context = context;
            if (Dna.Outfits.All(o => o.Context != context))
            {
                // A character made before this context existed: dress them for it.
                ulong seed = NextSeed();
                Commit(dna =>
                {
                    CharacterDna dressed = _generator.Reroll(dna, DnaCategories.Outfits, seed);
                    dna.Outfits.Add(dressed.Outfits.First(o => o.Context == context));
                    return dna;
                });
            }
            else Changed?.Invoke();
        }

        public string ShareCode => DnaSerializer.ToShareCode(Dna);

        public bool TryLoad(string shareCode, out string problem)
        {
            CharacterDna loaded;
            try { loaded = DnaSerializer.FromShareCode(shareCode); }
            catch (Exception e) when (e is FormatException || e is NotSupportedException)
            {
                problem = e.Message;
                return false;
            }
            // Made with other content? Keep what still fits and bring the rest up to date, rather than refusing it.
            CharacterDna repaired = DnaRepair.Repair(_generator, loaded, out List<string> fixes);
            Commit(_ => repaired);
            if (Outfit == null) Context = Dna.Outfits[0].Context;
            problem = fixes.Count == 0 ? null : "Loaded. Brought up to date: " + string.Join(", ", fixes.Distinct()) + ".";
            return true;
        }

        public bool Undo() => Step(_undo, _redo);
        public bool Redo() => Step(_redo, _undo);

        // ---------------------------------------------------------------- body, face, skin

        public float BodySlider(string id) => Dna.Body.Sliders.TryGetValue(id, out float value) ? value : 0f;

        public void SetBodySlider(string id, float value)
        {
            SliderEntry slider = _catalog.BodySliders.FirstOrDefault(s => s.Id == id) ?? throw new ArgumentException($"Unknown body slider '{id}'.");
            Commit(dna => { dna.Body.Sliders[id] = Clamp(value, slider.Min, slider.Max); return dna; }, "body." + id);
        }

        public void SetFemininity(float value) => Commit(dna => { dna.Body.Femininity = Clamp(value, 0f, 1f); return dna; }, "femininity");

        /// <summary>The face slider's actual value: the preset's value plus the stored offset.</summary>
        public float FaceSlider(string id)
        {
            Dna.Face.Sliders.TryGetValue(id, out float offset);
            return PresetValue(Dna.Face.PresetId, id) + offset;
        }

        public void SetFaceSlider(string id, float value)
        {
            SliderEntry slider = _catalog.FaceSliders.FirstOrDefault(s => s.Id == id) ?? throw new ArgumentException($"Unknown face slider '{id}'.");
            Commit(dna =>
            {
                dna.Face.Sliders[id] = Clamp(value, slider.Min, slider.Max) - PresetValue(dna.Face.PresetId, id);
                return dna;
            }, "face." + id);
        }

        /// <summary>Switches preset, keeping the player's tweaks (offsets) but clamping so every slider stays in range.</summary>
        public void SetFacePreset(string presetId)
        {
            if (_catalog.FacePresets.All(p => p.Id != presetId)) throw new ArgumentException($"Unknown face preset '{presetId}'.");
            Commit(dna =>
            {
                dna.Face.PresetId = presetId;
                foreach (SliderEntry slider in _catalog.FaceSliders)
                {
                    dna.Face.Sliders.TryGetValue(slider.Id, out float offset);
                    float preset = PresetValue(presetId, slider.Id);
                    dna.Face.Sliders[slider.Id] = Clamp(preset + offset, slider.Min, slider.Max) - preset;
                }
                return dna;
            });
        }

        public void SetEyeStyle(string id) => SetStyle(_catalog.EyeStyles, id, (dna, value) => dna.Face.EyeStyleId = value);
        public void SetBrowStyle(string id) => SetStyle(_catalog.BrowStyles, id, (dna, value) => dna.Face.BrowStyleId = value);

        public void SetIris(HsvColor colour, bool bothEyes = true) => Commit(dna =>
        {
            dna.Face.IrisLeft = colour.Normalized();
            if (bothEyes) dna.Face.IrisRight = dna.Face.IrisLeft;
            return dna;
        });

        /// <summary>The face mark worn in a "face." slot (freckles, a beauty mark, blush), or null.</summary>
        public string Mark(string slot) => Dna.Face.Makeup.FirstOrDefault(m => Makeup.SlotOf(_catalog, m.OverlayId) == slot)?.OverlayId;

        /// <summary>Puts on the face mark <paramref name="id"/> in <paramref name="slot"/>, or takes it off (null).</summary>
        public void SetMark(string slot, string id)
        {
            PartEntry part = id == null ? null : _catalog.Part(id);
            if (id != null && part?.Slot != slot) throw new ArgumentException($"'{id}' isn't a mark for '{slot}'.");
            Commit(dna =>
            {
                OverlayDna old = dna.Face.Makeup.FirstOrDefault(m => Makeup.SlotOf(_catalog, m.OverlayId) == slot);
                dna.Face.Makeup.RemoveAll(m => Makeup.SlotOf(_catalog, m.OverlayId) == slot);
                if (part == null) return dna;
                HsvColor tint = old?.Tint ?? default;
                ColourPalette blush = _catalog.Palette(Makeup.BlushPalette);
                if (slot == Slots.FaceBlush && old == null && blush != null) tint = HsvColor.FromHex(blush.Colours[0].Hex);
                dna.Face.Makeup.Add(new OverlayDna { OverlayId = id, Tint = tint, Opacity = 1f });
                return dna;
            });
        }

        /// <summary>The lipstick colour, or null without lipstick.</summary>
        public HsvColor? Lipstick => Dna.Face.Makeup.FirstOrDefault(m => m.OverlayId == Makeup.Lipstick)?.Tint;

        public void SetLipstick(HsvColor? colour) => Commit(dna =>
        {
            dna.Face.Makeup.RemoveAll(m => m.OverlayId == Makeup.Lipstick);
            if (colour.HasValue) dna.Face.Makeup.Add(new OverlayDna { OverlayId = Makeup.Lipstick, Tint = colour.Value.Normalized(), Opacity = 1f });
            return dna;
        });

        public void SetSkin(float tone, float undertone) => Commit(dna =>
        {
            dna.Skin.Tone = Clamp(tone, 0f, 1f);
            dna.Skin.Undertone = Clamp(undertone, 0f, 1f);
            return dna;
        }, "skin");

        // ---------------------------------------------------------------- hair

        /// <summary>Sets the hair piece for a hair slot; null removes it (only hair.front may be empty).</summary>
        public bool SetHairPiece(string slot, string id, out string problem)
        {
            if (id == null && slot == Slots.HairBack)
            {
                problem = "Hair needs a back piece.";
                return false;
            }
            if (id != null && _catalog.Part(id)?.Slot != slot)
            {
                problem = $"'{id}' isn't a {slot} piece.";
                return false;
            }
            return TryCommit(dna =>
            {
                dna.Hair.PieceIds.RemoveAll(piece => _catalog.Part(piece)?.Slot == slot);
                if (id != null) dna.Hair.PieceIds.Insert(slot == Slots.HairBack ? 0 : dna.Hair.PieceIds.Count, id);
                // A new cut takes off bangs it can't have (an afro has none).
                if (slot == Slots.HairBack && id != null)
                    dna.Hair.PieceIds.RemoveAll(piece => _catalog.Part(piece) is PartEntry front && front.Slot == Slots.HairFront
                        && !CharacterGenerator.Compatible(front, new[] { _catalog.Part(id) }));
                _generator.DropIncompatibleParts(dna); // and a hat or hair clip it can't carry, as at the salon
            }, out problem);
        }

        public void SetHairColours(HsvColor baseColour, HsvColor tip, float gradient) => Commit(dna =>
        {
            dna.Hair.Base = baseColour.Normalized();
            dna.Hair.Tip = tip.Normalized();
            dna.Hair.Gradient = Clamp(gradient, 0f, 1f);
            dna.Hair.Highlight = Colours.Shift(dna.Hair.Base, 0f, -0.1f, 0.18f);
            return dna;
        }, "hair.colours");

        // ---------------------------------------------------------------- outfit

        public ItemDna Item(string slot) => Outfit?.Items.FirstOrDefault(i => _catalog.Part(i.ItemId)?.Slot == slot);

        /// <summary>
        /// Wears <paramref name="id"/> in the current outfit, displacing whatever it blocks or is blocked by, and filling
        /// a slot it leaves bare (swapping a dress for a top adds a bottom). Null removes an optional item. Refused when
        /// the result breaks a rule, e.g. a hat over hair that can't take one.
        /// </summary>
        public bool SetItem(string slot, string id, out string problem)
        {
            PartEntry part = id == null ? null : _catalog.Part(id);
            if (id != null && part?.Slot != slot)
            {
                problem = $"'{id}' isn't a {slot} item.";
                return false;
            }

            return TryCommit(dna =>
            {
                OutfitDna outfit = dna.Outfits.First(o => o.Context == Context);
                ItemDna previous = outfit.Items.FirstOrDefault(i => _catalog.Part(i.ItemId)?.Slot == slot);
                if (part == null)
                {
                    if (previous != null) outfit.Items.Remove(previous);
                    return;
                }

                List<HsvColor> colours = previous?.RegionColors ?? outfit.Items.FirstOrDefault()?.RegionColors ?? new List<HsvColor>();
                outfit.Items.RemoveAll(item =>
                {
                    PartEntry other = _catalog.Part(item.ItemId);
                    return other == null || other.Slot == slot || part.BlocksSlots.Contains(other.Slot) || other.BlocksSlots.Contains(slot);
                });
                outfit.Items.Add(NewItem(part, colours));

                // Taking off a one-piece leaves the other half of the separates missing: add one.
                bool hasTop = outfit.Items.Any(i => _catalog.Part(i.ItemId)?.Slot == Slots.Top);
                bool hasBottom = outfit.Items.Any(i => _catalog.Part(i.ItemId)?.Slot == Slots.Bottom);
                if (hasTop != hasBottom)
                {
                    string missing = hasTop ? Slots.Bottom : Slots.Top;
                    List<PartEntry> worn = dna.Hair.PieceIds.Concat(outfit.Items.Select(i => i.ItemId)).Select(_catalog.Part).Where(p => p != null).ToList();
                    PartEntry filler = _catalog.PartsInSlot(missing).FirstOrDefault(p =>
                        (p.Contexts.Count == 0 || p.Contexts.Contains(Context)) && CharacterGenerator.Compatible(p, worn));
                    if (filler != null) outfit.Items.Add(NewItem(filler, colours));
                }
            }, out problem);
        }

        public void SetItemColour(string slot, int region, HsvColor colour)
        {
            Commit(dna =>
            {
                ItemDna item = dna.Outfits.First(o => o.Context == Context).Items.FirstOrDefault(i => _catalog.Part(i.ItemId)?.Slot == slot);
                if (item != null && region >= 0 && region < item.RegionColors.Count) item.RegionColors[region] = colour.Normalized();
                return dna;
            });
        }

        /// <summary>Makes the item in <paramref name="slot"/> in another of its fabrics (null: its own); a print it can't take goes.</summary>
        public void SetItemFabric(string slot, string fabricId) => Commit(dna =>
        {
            ItemDna item = dna.Outfits.First(o => o.Context == Context).Items.FirstOrDefault(i => _catalog.Part(i.ItemId)?.Slot == slot);
            PartEntry part = item == null ? null : _catalog.Part(item.ItemId);
            if (part == null || (fabricId != null && !part.Fabrics.Contains(fabricId))) return dna;
            item.FabricId = part.Fabrics.Count > 0 && fabricId == part.Fabrics[0] ? null : fabricId;
            if (item.PatternId != null && !_catalog.TakesPrint(part, item.FabricId, item.PatternId))
            {
                item.PatternId = null;
                item.PatternColor = null;
            }
            return dna;
        });

        public void SetItemPattern(string slot, string patternId, HsvColor colour, float scale) => Commit(dna =>
        {
            ItemDna item = dna.Outfits.First(o => o.Context == Context).Items.FirstOrDefault(i => _catalog.Part(i.ItemId)?.Slot == slot);
            PartEntry part = item == null ? null : _catalog.Part(item.ItemId);
            if (part == null || !part.Patternable || (patternId != null && !_catalog.TakesPrint(part, item.FabricId, patternId))) return dna;
            item.PatternId = patternId;
            item.PatternColor = patternId == null ? (HsvColor?)null : colour.Normalized();
            item.PatternScale = scale;
            return dna;
        });

        // ---------------------------------------------------------------- plumbing

        ItemDna NewItem(PartEntry part, List<HsvColor> colours)
        {
            var item = new ItemDna { ItemId = part.Id };
            for (int region = 0; region < part.Regions; region++)
                item.RegionColors.Add(colours.Count == 0 ? new HsvColor(0f, 0f, 0.7f) : colours[Math.Min(region, colours.Count - 1)]);
            return item;
        }

        void SetStyle(List<StyleEntry> styles, string id, Action<CharacterDna, string> set)
        {
            if (styles.All(s => s.Id != id)) throw new ArgumentException($"Unknown style '{id}'.");
            Commit(dna => { set(dna, id); return dna; });
        }

        float PresetValue(string presetId, string sliderId)
        {
            FacePreset preset = _catalog.FacePresets.FirstOrDefault(p => p.Id == presetId);
            return preset != null && preset.Values.TryGetValue(sliderId, out float value) ? value : 0f;
        }

        /// <summary>Applies an edit to a copy, keeping it only if the result is valid.</summary>
        bool TryCommit(Action<CharacterDna> edit, out string problem)
        {
            CharacterDna edited = Copy(Dna);
            edit(edited);
            List<string> errors = DnaValidator.Validate(_catalog, edited);
            if (errors.Count > 0)
            {
                problem = errors[0];
                return false;
            }
            problem = null;
            Commit(_ => edited);
            return true;
        }

        void Commit(Func<CharacterDna, CharacterDna> edit, string mergeKey = null)
        {
            string before = DnaSerializer.ToJson(Dna, indented: false);
            CharacterDna after = edit(Copy(Dna));
            if (after == null || DnaSerializer.ToJson(after, indented: false) == before) return;

            bool merge = mergeKey != null && mergeKey == _mergeKey && _undo.Count > 0;
            if (!merge)
            {
                _undo.Add(before);
                if (_undo.Count > MaxHistory) _undo.RemoveAt(0);
            }
            _mergeKey = mergeKey;
            _redo.Clear();
            Dna = after;
            Changed?.Invoke();
        }

        /// <summary>Ends a run of merged edits (call when a slider drag finishes), so the next drag gets its own undo step.</summary>
        public void EndGesture() => _mergeKey = null;

        bool Step(List<string> from, List<string> to)
        {
            if (from.Count == 0) return false;
            _mergeKey = null;
            to.Add(DnaSerializer.ToJson(Dna, indented: false));
            Dna = DnaSerializer.FromJson(from[from.Count - 1]);
            from.RemoveAt(from.Count - 1);
            if (Outfit == null) Context = Dna.Outfits[0].Context;
            Changed?.Invoke();
            return true;
        }

        ulong NextSeed() => StableHash.SplitMix64(_sessionSeed ^ StableHash.SplitMix64(++_rolls));

        static CharacterDna Copy(CharacterDna dna) => DnaSerializer.FromJson(DnaSerializer.ToJson(dna, indented: false));
        static float Clamp(float value, float min, float max) => value < min ? min : value > max ? max : value;
    }
}
