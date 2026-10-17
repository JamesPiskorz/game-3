using System;
using System.Collections.Generic;
using System.Linq;
using Game.Characters.Building;
using Game.Characters.Content;
using Game.Characters.Creator;
using Game.Characters.Dna;
using Game.Characters.Generation;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.Creator
{
    /// <summary>
    /// The character creator's UI (UI Toolkit), built from the catalog so new sliders, presets and parts appear
    /// without UI work. It only talks to <see cref="CreatorSession"/>; the screen rebuilds the 3D preview when the
    /// session changes. Values refresh in place; a tab rebuilds only when its structure changes (items worn).
    /// </summary>
    public sealed class CreatorView
    {
        static readonly (string Id, string Label, DnaCategories Category)[] Tabs =
        {
            ("body", "Body", DnaCategories.Body),
            ("face", "Face", DnaCategories.Face),
            ("skin", "Skin & eyes", DnaCategories.Skin),
            ("hair", "Hair", DnaCategories.Hair),
            ("outfit", "Outfit", DnaCategories.Outfits),
        };

        static readonly (string Slot, bool Optional)[] OutfitSlots =
        {
            (Slots.Top, false), (Slots.Bottom, false), (Slots.OnePiece, true), (Slots.Shoes, false), (Slots.Hat, true), (Slots.Glasses, true), (Slots.Neck, true), (Slots.Outer, true), (Slots.Legwear, true), (Slots.Earrings, true), (Slots.Bag, true), (Slots.HairAccessory, true),
        };

        static readonly string[] RegionNames = { "Main", "Trim", "Detail", "Accent" };

        readonly CreatorSession _session;
        readonly ContentCatalog _catalog;
        readonly Action<string> _focus;
        readonly Action<float> _rotate;
        readonly VisualElement _root;
        readonly Dictionary<string, Slider> _sliders = new Dictionary<string, Slider>();
        readonly Dictionary<string, Button> _tabButtons = new Dictionary<string, Button>();
        readonly Dictionary<string, Button> _contextButtons = new Dictionary<string, Button>();
        ScrollView _content;
        Button _undo, _redo;
        TextField _shareCode;
        Label _status, _styleRead;
        string _tab = "body";
        string _structure;

        readonly Action _play;
        readonly Action<string> _express;
        readonly Func<CharacterDna, int, UnityEngine.Texture> _thumbnail;
        VisualElement _variations;
        List<CharacterDna> _candidates;

        public const int VariationCount = 6;

        /// <param name="play">Leaves the creator for the game with this look; null hides the button.</param>
        /// <param name="express">Shows a facial expression on the preview; null hides the buttons.</param>
        /// <param name="thumbnail">Renders a candidate into thumbnail slot n, for the Variations strip; null hides it.</param>
        public CreatorView(VisualElement root, CreatorSession session, Action<string> focus, Action<float> rotate, Action play = null, Action<string> express = null,
            Func<CharacterDna, int, UnityEngine.Texture> thumbnail = null)
        {
            _thumbnail = thumbnail;
            _play = play;
            _express = express;
            _root = root;
            _session = session;
            _catalog = session.Catalog;
            _focus = focus;
            _rotate = rotate;
            Build();
            session.Changed += Refresh;
            Refresh();
        }

        // ---------------------------------------------------------------- variations

        void ToggleVariations()
        {
            bool show = _variations.style.display == DisplayStyle.None;
            _variations.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (show) ShowVariations();
        }

        void ShowVariations()
        {
            _variations.Clear();
            _candidates = _session.Variations(VariationCount);
            var grid = new VisualElement();
            grid.AddToClassList("variation-grid");
            _variations.Add(grid);
            for (int i = 0; i < _candidates.Count; i++)
            {
                CharacterDna candidate = _candidates[i];
                var pick = new Button(() => _session.Adopt(candidate)) { name = "variation-" + i, text = "" };
                pick.AddToClassList("variation");
                if (_thumbnail(candidate, i) is UnityEngine.RenderTexture texture) pick.style.backgroundImage = Background.FromRenderTexture(texture);
                pick.tooltip = candidate.Style ?? "";
                grid.Add(pick);
            }
            _variations.Add(TextButton("More like this", "variations-more", ShowVariations));
        }

        // ---------------------------------------------------------------- layout

        void Build()
        {
            _root.Clear();
            _root.AddToClassList("creator");

            var panel = new VisualElement { name = "panel" };
            panel.AddToClassList("panel");
            _root.Add(panel);

            var title = new Label("Create a character");
            title.AddToClassList("title");
            panel.Add(title);

            if (_play != null)
            {
                var naming = Row("naming");
                naming.Add(new Label("Your name:") { name = "name-label" });
                var name = new TextField { name = "player-name", value = PlayerLook.Name ?? "", maxLength = PlayerLook.MaxNameLength };
                name.AddToClassList("share-code");
                name.RegisterValueChangedCallback(e => PlayerLook.Name = e.newValue);
                naming.Add(name);
                panel.Add(naming);
            }

            var actions = Row("actions");
            actions.Add(TextButton("Randomize", "randomize", () => _session.Randomize(), "primary"));
            actions.Add(TextButton("Sibling", "sibling", () => _session.Sibling()));
            _undo = TextButton("Undo", "undo", () => _session.Undo());
            _redo = TextButton("Redo", "redo", () => _session.Redo());
            actions.Add(_undo);
            actions.Add(_redo);
            if (_play != null) actions.Add(TextButton("Into town →", "play", _play, "primary"));
            panel.Add(actions);

            // Variations: six characters Randomize could give, side by side; click one to take it.
            if (_thumbnail != null)
            {
                actions.Insert(1, TextButton("Variations", "variations", ToggleVariations));
                _variations = new VisualElement { name = "variations" };
                _variations.AddToClassList("variations");
                _variations.style.display = DisplayStyle.None;
                panel.Add(_variations);
            }

            // Which style Randomize aims for: choosing one randomizes straight away.
            var styles = Row("styles");
            styles.Add(new Label("Style:") { name = "styles-label" });
            var chips = new List<Button>();
            void Choose(string style)
            {
                _session.Style = style;
                for (int i = 0; i < chips.Count; i++)
                    chips[i].EnableInClassList("primary", (i == 0 ? null : _catalog.Archetypes[i - 1].Id) == style);
                _session.Randomize();
            }
            chips.Add(TextButton("Any", "style-any", () => Choose(null)));
            foreach (StyleArchetype archetype in _catalog.Archetypes)
            {
                string id = archetype.Id;
                chips.Add(TextButton(archetype.Name ?? id, "style-" + id, () => Choose(id)));
            }
            chips[0].AddToClassList("primary");
            foreach (Button chip in chips)
            {
                chip.AddToClassList("style-chip");
                styles.Add(chip);
            }
            panel.Add(styles);

            var locks = Row("locks");
            locks.Add(new Label("Keep when randomizing:") { name = "locks-label" });
            foreach ((string id, string label, DnaCategories category) in Tabs)
            {
                var toggle = new Toggle(label) { name = "lock-" + id, value = (_session.Locks & category) != 0 };
                toggle.AddToClassList("lock");
                toggle.RegisterValueChangedCallback(e => _session.Locks = e.newValue ? _session.Locks | category : _session.Locks & ~category);
                locks.Add(toggle);
            }
            panel.Add(locks);

            var tabs = Row("tabs");
            foreach ((string id, string label, _) in Tabs)
            {
                Button button = TextButton(label, "tab-" + id, () => ShowTab(id), "tab");
                _tabButtons[id] = button;
                tabs.Add(button);
            }
            panel.Add(tabs);

            _content = new ScrollView(ScrollViewMode.Vertical) { name = "tab-content" };
            _content.AddToClassList("tab-content");
            panel.Add(_content);

            var footer = new VisualElement { name = "footer" };
            footer.AddToClassList("footer");
            var contexts = Row("contexts");
            contexts.Add(new Label("Dressed for:"));
            foreach (OutfitContext context in _catalog.Contexts)
            {
                string id = context.Id;
                Button button = TextButton(context.Name ?? ContentCatalog.Humanize(id), "context-" + id, () => _session.SetContext(id), "chip");
                _contextButtons[id] = button;
                contexts.Add(button);
            }
            footer.Add(contexts);

            var share = Row("share");
            _shareCode = new TextField { name = "share-code", isDelayed = true };
            _shareCode.AddToClassList("share-code");
            share.Add(_shareCode);
            share.Add(TextButton("Copy", "copy", () => GUIUtility.systemCopyBuffer = _session.ShareCode));
            share.Add(TextButton("Load", "load", () =>
            {
                if (!_session.TryLoad(_shareCode.value, out string problem)) ShowStatus(problem, error: true);
                else ShowStatus(problem ?? "Loaded.", error: false);
            }));
            footer.Add(share);
            _styleRead = new Label { name = "style-read" };
            _styleRead.AddToClassList("status");
            footer.Add(_styleRead);
            _status = new Label { name = "status" };
            _status.AddToClassList("status");
            footer.Add(_status);
            panel.Add(footer);

            var preview = new VisualElement { name = "preview" };
            preview.AddToClassList("preview");
            Vector2? drag = null;
            preview.RegisterCallback<PointerDownEvent>(e => { drag = e.position; preview.CapturePointer(e.pointerId); });
            preview.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (drag == null) return;
                _rotate(-(e.position.x - drag.Value.x) * 0.4f);
                drag = e.position;
            });
            preview.RegisterCallback<PointerUpEvent>(e => { drag = null; preview.ReleasePointer(e.pointerId); });
            var views = Row("views");
            views.AddToClassList("views");
            views.Add(TextButton("Full body", "view-body", () => _focus("body"), "chip"));
            views.Add(TextButton("Face", "view-face", () => _focus("face"), "chip"));
            if (_express != null)
                foreach ((string id, string label) in new[] { ("happy", "Smile"), ("shy", "Shy"), ("surprised", "Surprised"), ("sad", "Sad") })
                    views.Add(TextButton(label, "expr-" + id, () => _express(id), "chip"));
            preview.Add(views);
            _root.Add(preview);

            ShowTab(_tab);
        }

        void ShowTab(string tab)
        {
            _tab = tab;
            foreach (KeyValuePair<string, Button> pair in _tabButtons)
                pair.Value.EnableInClassList("selected", pair.Key == tab);
            if (tab == "face") _focus("face");
            else if (tab == "body" || tab == "outfit") _focus("body");
            RebuildTab();
        }

        void RebuildTab()
        {
            _content.Clear();
            _sliders.Clear();
            switch (_tab)
            {
                case "body": BuildBody(); break;
                case "face": BuildFace(); break;
                case "skin": BuildSkin(); break;
                case "hair": BuildHair(); break;
                case "outfit": BuildOutfit(); break;
            }
            _structure = Structure();
        }

        // ---------------------------------------------------------------- tabs

        void BuildBody()
        {
            AddSlider("femininity", "Presentation", 0f, 1f, () => _session.Dna.Body.Femininity, v => _session.SetFemininity(v));
            foreach (SliderEntry slider in _catalog.BodySliders)
            {
                string id = slider.Id;
                AddSlider("body." + id, slider.Name ?? ContentCatalog.Humanize(id), slider.Min, slider.Max, () => _session.BodySlider(id), v => _session.SetBodySlider(id, v));
            }
            AddReroll(DnaCategories.Body, "Reroll body");
        }

        void BuildFace()
        {
            AddDropdown("preset", "Face", _catalog.FacePresets.Select(p => (p.Id, p.Name ?? ContentCatalog.Humanize(p.Id))).ToList(),
                () => _session.Dna.Face.PresetId, id => _session.SetFacePreset(id));
            AddDropdown("eye-style", "Eyes", _catalog.EyeStyles.Select(s => (s.Id, s.Name ?? ContentCatalog.Humanize(s.Id))).ToList(),
                () => _session.Dna.Face.EyeStyleId, id => _session.SetEyeStyle(id));
            AddDropdown("brow-style", "Brows", _catalog.BrowStyles.Select(s => (s.Id, s.Name ?? ContentCatalog.Humanize(s.Id))).ToList(),
                () => _session.Dna.Face.BrowStyleId, id => _session.SetBrowStyle(id));
            foreach (SliderEntry slider in _catalog.FaceSliders)
            {
                string id = slider.Id;
                AddSlider("face." + id, slider.Name ?? ContentCatalog.Humanize(id), slider.Min, slider.Max, () => _session.FaceSlider(id), v => _session.SetFaceSlider(id, v));
            }

            // Marks and makeup: freckles, a beauty mark, blush and lipstick.
            foreach ((string slot, string label) in new[] { (Slots.FaceFreckles, "Freckles"), (Slots.FaceMole, "Beauty mark"), (Slots.FaceBlush, "Blush") })
            {
                var choices = new List<(string Id, string Name)> { (null, "None") };
                choices.AddRange(_catalog.PartsInSlot(slot).Select(p => (p.Id, p.Name ?? ContentCatalog.Humanize(p.Id))));
                AddDropdown("mark-" + slot, label, choices, () => _session.Mark(slot), id => _session.SetMark(slot, id));
            }
            List<HsvColor> lips = Palettes(Makeup.LipPalette, "makeup.lips.dark");
            AddDropdown("lipstick", "Lipstick", new List<(string Id, string Name)> { (null, "None"), ("on", "Wearing it") },
                () => _session.Lipstick.HasValue ? "on" : null, on => _session.SetLipstick(on == null ? (HsvColor?)null : _session.Lipstick ?? lips[0]));
            AddSwatches("Lipstick colour", lips, () => _session.Lipstick ?? default, c => _session.SetLipstick(c), compact: true);
            AddReroll(DnaCategories.Face, "Reroll face");
        }

        void BuildSkin()
        {
            AddSlider("skin.tone", "Skin tone", 0f, 1f, () => _session.Dna.Skin.Tone, v => _session.SetSkin(v, _session.Dna.Skin.Undertone));
            AddSlider("skin.undertone", "Undertone (cool – warm)", 0f, 1f, () => _session.Dna.Skin.Undertone, v => _session.SetSkin(_session.Dna.Skin.Tone, v));
            AddSwatches("Eye colour", Palettes(_catalog.Eyes.NaturalPalette, _catalog.Eyes.FantasyPalette), () => _session.Dna.Face.IrisLeft, c => _session.SetIris(c));
            AddReroll(DnaCategories.Skin, "Reroll skin");
        }

        void BuildHair()
        {
            AddDropdown("hair-back", "Style", Parts(Slots.HairBack, optional: false), () => Piece(Slots.HairBack), id => Report(_session.SetHairPiece(Slots.HairBack, id, out string p), p));
            AddDropdown("hair-front", "Bangs", Parts(Slots.HairFront, optional: true), () => Piece(Slots.HairFront), id => Report(_session.SetHairPiece(Slots.HairFront, id, out string p), p));
            List<HsvColor> hairColours = Palettes(_catalog.Hair.NaturalPalette, _catalog.Hair.FantasyPalette);
            AddSwatches("Colour", hairColours, () => _session.Dna.Hair.Base, c => _session.SetHairColours(c, _session.Dna.Hair.Gradient > 0f ? _session.Dna.Hair.Tip : c, _session.Dna.Hair.Gradient));
            AddSwatches("Tips", hairColours, () => _session.Dna.Hair.Tip, c => _session.SetHairColours(_session.Dna.Hair.Base, c, Math.Max(_session.Dna.Hair.Gradient, 0.8f)));
            AddSlider("hair.gradient", "Tip fade", 0f, 1f, () => _session.Dna.Hair.Gradient, v => _session.SetHairColours(_session.Dna.Hair.Base, _session.Dna.Hair.Tip, v));
            AddReroll(DnaCategories.Hair, "Reroll hair");
        }

        void BuildOutfit()
        {
            List<HsvColor> clothing = Palettes(_catalog.Palettes.Where(p => p.Id.StartsWith("outfit.", StringComparison.Ordinal)).Select(p => p.Id).ToArray());
            foreach ((string slot, bool optional) in OutfitSlots)
            {
                List<(string, string)> choices = Parts(slot, optional || slot == Slots.Top || slot == Slots.Bottom, _session.Context);
                if (choices.Count <= 1 && optional) continue;
                string label = slot == Slots.OnePiece ? "One-piece" : ContentCatalog.Humanize(slot);
                AddDropdown("item-" + slot, label, choices, () => _session.Item(slot)?.ItemId, id =>
                {
                    // Choosing "None" for a required half of separates means switching to the other option.
                    if (id == null && (slot == Slots.Top || slot == Slots.Bottom))
                    {
                        ShowStatus("Pick a one-piece to go without separates.", error: true);
                        Refresh();
                        return;
                    }
                    Report(_session.SetItem(slot, id, out string problem), problem);
                });

                ItemDna item = _session.Item(slot);
                PartEntry part = item == null ? null : _catalog.Part(item.ItemId);
                if (part == null) continue;
                for (int region = 0; region < item.RegionColors.Count; region++)
                {
                    int r = region;
                    AddSwatches($"{label}: {RegionNames[Math.Min(r, RegionNames.Length - 1)].ToLowerInvariant()}", clothing,
                        () => _session.Item(slot)?.RegionColors[r] ?? default, c => _session.SetItemColour(slot, r, c), compact: true);
                }
                if (part.Patternable)
                {
                    var patterns = new List<(string, string)> { (null, "Plain") };
                    patterns.AddRange(_catalog.Patterns.Select(p => (p.Id, p.Name ?? ContentCatalog.Humanize(p.Id))));
                    AddDropdown("pattern-" + slot, $"{label}: pattern", patterns, () => _session.Item(slot)?.PatternId, id =>
                    {
                        ItemDna current = _session.Item(slot);
                        HsvColor colour = current?.PatternColor ?? (current?.RegionColors.Count > 1 ? current.RegionColors[1] : new HsvColor(0f, 0f, 0.95f));
                        PatternEntry pattern = _catalog.Patterns.FirstOrDefault(p => p.Id == id);
                        _session.SetItemPattern(slot, id, colour, pattern == null ? 1f : (pattern.MinScale + pattern.MaxScale) * 0.5f);
                    });
                }
            }
            AddReroll(DnaCategories.Outfits, "Reroll outfits");
        }

        // ---------------------------------------------------------------- controls

        void AddSlider(string key, string label, float min, float max, Func<float> get, Action<float> set)
        {
            var slider = new Slider(label, min, max) { name = key, value = get() };
            slider.AddToClassList("field");
            slider.RegisterValueChangedCallback(e => set(e.newValue));
            // A finished drag closes its undo step; the drag itself happens on a child, so listen on the way down.
            slider.RegisterCallback<PointerUpEvent>(_ => _session.EndGesture(), TrickleDown.TrickleDown);
            slider.userData = get;
            _sliders[key] = slider;
            _content.Add(slider);
        }

        void AddDropdown(string key, string label, List<(string Id, string Name)> choices, Func<string> get, Action<string> set)
        {
            var dropdown = new DropdownField(label, choices.Select(c => c.Name).ToList(), 0) { name = key };
            dropdown.AddToClassList("field");
            dropdown.SetValueWithoutNotify(choices.FirstOrDefault(c => c.Id == get()).Name ?? choices[0].Name);
            dropdown.RegisterValueChangedCallback(e =>
            {
                int index = choices.FindIndex(c => c.Name == e.newValue);
                if (index >= 0) set(choices[index].Id);
            });
            dropdown.userData = (Func<string>)(() => choices.FirstOrDefault(c => c.Id == get()).Name ?? choices[0].Name);
            _content.Add(dropdown);
        }

        void AddSwatches(string label, List<HsvColor> colours, Func<HsvColor> get, Action<HsvColor> set, bool compact = false)
        {
            var block = new VisualElement();
            block.AddToClassList("swatch-block");
            block.Add(new Label(label));
            var row = new VisualElement();
            row.AddToClassList("swatches");
            if (compact) row.AddToClassList("compact");
            foreach (HsvColor colour in colours)
            {
                var swatch = new Button(() => set(colour));
                swatch.AddToClassList("swatch");
                swatch.style.backgroundColor = Color.HSVToRGB(colour.H, colour.S, colour.V);
                swatch.userData = colour;
                row.Add(swatch);
            }
            row.userData = get;
            block.Add(row);
            _content.Add(block);
        }

        void AddReroll(DnaCategories category, string label)
        {
            Button button = TextButton(label, "reroll", () => _session.Reroll(category));
            button.AddToClassList("reroll");
            _content.Add(button);
        }

        static Button TextButton(string text, string name, Action click, string style = null)
        {
            var button = new Button(click) { text = text, name = name };
            if (style != null) button.AddToClassList(style);
            return button;
        }

        static VisualElement Row(string name)
        {
            var row = new VisualElement { name = name };
            row.AddToClassList("row");
            return row;
        }

        // ---------------------------------------------------------------- refresh

        void Refresh()
        {
            if (Structure() != _structure) RebuildTab();

            foreach (Slider slider in _sliders.Values)
                slider.SetValueWithoutNotify(((Func<float>)slider.userData)());
            _content.Query<DropdownField>().ForEach(d => d.SetValueWithoutNotify(((Func<string>)d.userData)()));
            _content.Query<VisualElement>(className: "swatches").ForEach(row =>
            {
                HsvColor selected = ((Func<HsvColor>)row.userData)();
                foreach (VisualElement swatch in row.Children())
                    swatch.EnableInClassList("selected", HsvColor.Distance((HsvColor)swatch.userData, selected) < 0.06f);
            });

            _undo.SetEnabled(_session.CanUndo);
            _redo.SetEnabled(_session.CanRedo);
            foreach (KeyValuePair<string, Button> pair in _contextButtons)
                pair.Value.EnableInClassList("selected", pair.Key == _session.Context);
            _shareCode.SetValueWithoutNotify(_session.ShareCode);

            List<string> reads = _session.StyleReads();
            _styleRead.text = reads.Count == 0 ? "" :
                $"This look reads as {string.Join(" and ", reads.Select(r => (_catalog.Archetype(r)?.Name ?? r).ToLowerInvariant()))}: people into that style will notice.";
        }

        // What decides a tab's layout: the items worn (outfit tab) and context.
        string Structure() => _tab == "outfit"
            ? _session.Context + ":" + string.Join(",", _session.Outfit?.Items.Select(i => i.ItemId + (_catalog.Part(i.ItemId)?.Patternable == true ? "p" : "")) ?? Enumerable.Empty<string>())
            : _tab;

        void Report(bool ok, string problem)
        {
            if (!ok)
            {
                ShowStatus(problem, error: true);
                Refresh();
            }
            else ShowStatus("", error: false);
        }

        void ShowStatus(string message, bool error)
        {
            _status.text = message ?? "";
            _status.EnableInClassList("error", error);
        }

        // ---------------------------------------------------------------- data helpers

        List<(string, string)> Parts(string slot, bool optional, string context = null)
        {
            var choices = new List<(string, string)>();
            if (optional) choices.Add((null, "None"));
            choices.AddRange(_catalog.PartsInSlot(slot)
                .Where(p => context == null || p.Contexts.Count == 0 || p.Contexts.Contains(context))
                .Select(p => (p.Id, p.Name ?? ContentCatalog.Humanize(p.Id))));
            return choices;
        }

        string Piece(string slot) => _session.Dna.Hair.PieceIds.FirstOrDefault(id => _catalog.Part(id)?.Slot == slot);

        List<HsvColor> Palettes(params string[] ids)
        {
            var colours = new List<HsvColor>();
            foreach (string id in ids)
                foreach (PaletteColour colour in _catalog.Palette(id)?.Colours ?? new List<PaletteColour>())
                {
                    HsvColor hsv = HsvColor.FromHex(colour.Hex);
                    if (colours.All(c => HsvColor.Distance(c, hsv) > 0.04f)) colours.Add(hsv);
                }
            return colours;
        }
    }
}
