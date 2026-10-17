using System;
using System.Collections.Generic;
using System.Linq;
using Game.Characters.Content;
using Game.Characters.Dna;
using Game.Sim;
using Game.Sim.Relationships;
using Game.Sim.Residents;
using Game.Sim.Sandbox;
using UnityEngine.UIElements;

namespace Game.Town
{
    /// <summary>
    /// The town HUD, built in code with UI Toolkit: the clock and place along the top, the latest exchange in a
    /// dialogue box, and a side panel to travel, pick who to talk to, see how they feel and act. Every control has a
    /// name ("place-cafe", "here-0", "act-talk", "topic-music"...) so captures and tests can press it.
    /// </summary>
    public sealed class TownView
    {
        readonly TownSession _session;
        readonly Label _clock, _place, _coins, _notice, _speaker, _line, _followUp, _learnt;
        readonly VisualElement _dialogue, _deltas, _places, _here, _card, _clothes;
        readonly Button _work, _contactsButton;
        readonly Label _hereTitle;
        readonly VisualElement _contacts;
        bool _showContacts;
        string _choices; // "talk", "gift" or "date": which picker is open under the actions
        Resident _choicesFor;

        public TownView(VisualElement root, TownSession session, Action save, Action load)
        {
            _session = session;
            root.AddToClassList("town");

            var main = Add(root, new VisualElement(), "main");
            var top = Add(main, new VisualElement(), "topbar");
            _clock = Add(top, new Label(), "clock");
            _place = Add(top, new Label(), "place");
            _coins = Add(top, new Label(), "coins");
            Add(top, new VisualElement(), "spacer");
            _work = Add(top, Button("work", "Work a shift", () => _session.Work()));
            _contactsButton = Add(top, Button("contacts", "Contacts", () =>
            {
                _showContacts = !_showContacts;
                Refresh();
            }));
            Add(top, Button("wait", "Wait an hour", () => _session.Wait()));
            Add(top, Button("sleep", "Sleep", () => _session.Sleep()));
            Add(top, Button("save", "Save", save));
            Add(top, Button("load", "Load", load));
            _notice = Add(main, new Label(), "notice");
            Add(main, new VisualElement(), "spacer");
            _dialogue = Add(main, new VisualElement(), "dialogue");
            _speaker = Add(_dialogue, new Label(), "speaker");
            _line = Add(_dialogue, new Label(), "line");
            _followUp = Add(_dialogue, new Label(), "line", "follow-up");
            _deltas = Add(_dialogue, new VisualElement(), "deltas");
            _learnt = Add(_dialogue, new Label(), "learnt");

            var panel = Add(root, new ScrollView(), "panel");
            Add(panel, new Label("Go to"), "section");
            _places = Add(panel, new VisualElement(), "chips");
            _contacts = Add(panel, new VisualElement(), "contacts");
            _hereTitle = Add(panel, new Label("Here"), "section");
            _here = Add(panel, new VisualElement(), "here");
            _clothes = Add(panel, new VisualElement(), "clothes");
            _card = Add(panel, new VisualElement(), "card");

            session.Changed += Refresh;
            Refresh();
        }

        public void Refresh()
        {
            SimWorld world = _session.World;
            _clock.text = world.Now.ToString();
            _place.text = _session.LocationName(_session.Location) + (_session.DateWith != null ? $"  ·  on a date with {_session.DateWith.Profile.FirstName}" : "");
            _coins.text = $"{world.Player.Money} coins";
            _work.style.display = _session.CanWork ? DisplayStyle.Flex : DisplayStyle.None;
            _notice.text = _session.Notice ?? "";
            _notice.style.display = _session.Notice == null ? DisplayStyle.None : DisplayStyle.Flex;

            RefreshDialogue();
            RefreshPlaces(world);
            RefreshContacts();
            DisplayStyle normal = _showContacts ? DisplayStyle.None : DisplayStyle.Flex;
            _hereTitle.style.display = _here.style.display = _clothes.style.display = normal;
            if (_showContacts) _card.style.display = DisplayStyle.None;
            if (_showContacts) return;
            RefreshHere();
            RefreshClothes(world);
            RefreshCard(world);
        }

        /// <summary>Everyone you've met, closest first, with what you know about where they are.</summary>
        void RefreshContacts()
        {
            _contacts.Clear();
            _contactsButton.EnableInClassList("selected", _showContacts);
            _contacts.style.display = _showContacts ? DisplayStyle.Flex : DisplayStyle.None;
            if (!_showContacts) return;

            Add(_contacts, new Label("Contacts"), "section");
            List<Contact> contacts = _session.Contacts();
            if (contacts.Count == 0) Add(_contacts, new Label("You haven't met anyone yet. Say hi to people around town."), "hint");
            for (int i = 0; i < contacts.Count; i++)
            {
                Contact contact = contacts[i];
                var row = Add(_contacts, new VisualElement(), "contact");
                bool findable = contact.KnownLocation != null && contact.KnownLocation != SimCatalog.Home && _session.IsOpen(contact.KnownLocation);
                Button button = Add(row, Button("contact-" + i, $"{contact.Resident.Name}   \u00B7   {StageName(contact.Relationship.Stage)}", () =>
                {
                    _showContacts = false;
                    _session.Find(contact);
                }), "person");
                button.SetEnabled(findable && _session.DateWith == null);
                Add(row, new Label(contact.Hint), "hint", "contact-hint");
            }
        }

        /// <summary>The boutique's stock at the mall, the wardrobe at home.</summary>
        void RefreshClothes(SimWorld world)
        {
            _clothes.Clear();
            List<ShopItem> shop = _session.Shop();
            if (shop.Count > 0)
            {
                Add(_clothes, new Label("Boutique"), "section");
                var stock = Add(_clothes, new VisualElement(), "chips");
                for (int i = 0; i < shop.Count; i++)
                {
                    ShopItem item = shop[i];
                    bool owned = _session.Owns(item.Item);
                    string style = world.Characters.Archetype(item.Style)?.Name ?? item.Style;
                    string label = $"{item.Name}  \u00B7  {style}  \u00B7  {(owned ? "owned" : item.Price.ToString())}";
                    Button button = Add(stock, Garment("buy-" + i, label, item.Item, () => _session.Buy(item)), "chip");
                    button.SetEnabled(!owned && world.Player.Money >= item.Price);
                }
            }
            if (_session.AtHome && _session.DateWith == null)
            {
                List<ItemDna> wardrobe = _session.Changeable();
                Add(_clothes, new Label("Wardrobe"), "section");
                if (wardrobe.Count == 0) Add(_clothes, new Label("Everything you own is on. The boutique at Arcade Lane has more."), "hint");
                var row = Add(_clothes, new VisualElement(), "chips");
                for (int i = 0; i < wardrobe.Count; i++)
                {
                    ItemDna item = wardrobe[i];
                    PartEntry part = world.Characters.Part(item.ItemId);
                    Add(row, Garment("wear-" + i, part?.Name ?? ContentCatalog.Humanize(item.ItemId), item, () => _session.Wear(item)), "chip");
                }
            }
        }

        /// <summary>A button showing a piece of clothing: a dot of its main colour, then its label.</summary>
        static Button Garment(string name, string text, ItemDna item, Action clicked)
        {
            Button button = Button(name, "", clicked);
            button.AddToClassList("garment");
            button.Add(Swatch(item));
            button.Add(new Label(text));
            return button;
        }

        static VisualElement Swatch(ItemDna item)
        {
            var swatch = new VisualElement();
            swatch.AddToClassList("swatch");
            if (item.RegionColors.Count > 0)
            {
                HsvColor c = item.RegionColors[0];
                swatch.style.backgroundColor = UnityEngine.Color.HSVToRGB(c.H - (float)Math.Floor(c.H), Math.Max(0f, Math.Min(1f, c.S)), Math.Max(0f, Math.Min(1f, c.V)));
            }
            return swatch;
        }

        void RefreshDialogue()
        {
            InteractionResult last = _session.Last;
            _dialogue.style.display = last == null ? DisplayStyle.None : DisplayStyle.Flex;
            if (last == null) return;

            _speaker.text = _session.Speaker.Name;
            _line.text = "“" + last.Line + "”";
            _followUp.text = last.FollowUp == null ? "" : "“" + last.FollowUp + "”";
            _followUp.style.display = last.FollowUp == null ? DisplayStyle.None : DisplayStyle.Flex;
            _learnt.text = last.Learnt ?? "";
            _learnt.style.display = last.Learnt == null ? DisplayStyle.None : DisplayStyle.Flex;

            _deltas.Clear();
            void Delta(string meter, float amount)
            {
                if (Math.Abs(amount) < 0.05f) return;
                Add(_deltas, new Label($"{(amount > 0 ? "+" : "−")}{Math.Abs(amount):0.#} {meter}"), "delta", amount > 0 ? "up" : "down");
            }
            Delta("affection", last.Affection);
            Delta("attraction", last.Attraction);
            Delta("familiarity", last.Familiarity);
            Delta("trust", last.Trust);
            if (last.StageChanged) Add(_deltas, new Label("Now: " + StageName(last.After)), "delta", "stage");
        }

        void RefreshPlaces(SimWorld world)
        {
            _places.Clear();
            var places = world.Catalog.Locations.Select(l => l.Id).Prepend(SimCatalog.Home);
            foreach (string id in places)
            {
                int count = id == SimCatalog.Home ? 0 : world.At(id).Count();
                bool open = _session.IsOpen(id);
                string label = _session.LocationName(id) + (open ? count > 0 ? $"  ·  {count}" : "" : "  (closed)");
                Button button = Add(_places, Button("place-" + id, label, () => _session.Travel(id)), "chip");
                button.SetEnabled(open);
                if (id == _session.Location) button.AddToClassList("selected");
            }
        }

        void RefreshHere()
        {
            _here.Clear();
            List<Resident> present = _session.Present();
            if (present.Count == 0)
            {
                Add(_here, new Label(_session.AtHome ? "You're at home. Head out, or sleep till morning." : "Nobody's around right now."), "hint");
                return;
            }
            for (int i = 0; i < present.Count; i++)
            {
                Resident resident = present[i];
                Relationship rel = _session.World.RelationshipWith(resident);
                string stage = rel.Met ? StageName(rel.Stage) : "New face";
                Button button = Add(_here, Button("here-" + i, $"{resident.Name}   ·   {stage}", () => _session.Select(resident)), "person");
                if (resident == _session.Selected) button.AddToClassList("selected");
            }
        }

        void RefreshCard(SimWorld world)
        {
            _card.Clear();
            Resident selected = _session.Selected;
            if (selected != _choicesFor) _choices = null; // pickers close when you turn to someone else
            _choicesFor = selected;
            _card.style.display = selected == null ? DisplayStyle.None : DisplayStyle.Flex;
            if (selected == null) return;

            var profile = selected.Profile;
            Relationship rel = world.RelationshipWith(selected);
            Add(_card, new Label(selected.Name), "card-name");
            string job = world.Catalog.Job(profile.JobId)?.Name ?? "";
            Add(_card, new Label($"{profile.Subject}/{profile.Object}  ·  {profile.Age}  ·  {job}"), "card-sub");
            Add(_card, new Label(rel.Met ? StageName(rel.Stage) : "You haven't met yet"), "stage");
            if (rel.Familiarity >= StyleHintFamiliarity) Add(_card, new Label(StyleHint(profile, world)), "known", "style");

            Meter("Affection", rel.Affection, "affection");
            Meter("Attraction", rel.Attraction, "attraction");
            Meter("Familiarity", rel.Familiarity, "familiarity");
            Meter("Trust", rel.Trust, "trust");

            string Topics(IEnumerable<string> ids) => string.Join(", ", ids.Select(id => world.Catalog.Topic(id)?.Name ?? id));
            if (rel.KnownLikes.Count + rel.KnownDislikes.Count == 0)
                Add(_card, new Label("Talk to find out what they're into."), "hint");
            if (rel.KnownLikes.Count > 0) Add(_card, new Label("Loves " + Topics(rel.KnownLikes)), "known", "liked");
            if (rel.KnownDislikes.Count > 0) Add(_card, new Label("Not into " + Topics(rel.KnownDislikes)), "known", "disliked");

            if (_session.Date != null)
            {
                DateCard(world, rel);
                return;
            }

            var actions = Add(_card, new VisualElement(), "actions");
            if (_session.DateWith == null) Add(actions, Button("act-greet", "Say hi", () => _session.Greet()));
            Add(actions, Picker("act-talk", "Talk about…", "talk"));
            Add(actions, Button("act-compliment", "Compliment", () => _session.Compliment()));
            if (_session.DateWith == null)
            {
                Add(actions, Picker("act-gift", "Give…", "gift"));
                Add(actions, Picker("act-date", "Ask out…", "date"));
            }
            else
            {
                Add(actions, Button("act-end-date", "Say goodnight", () => _session.EndDate()), "primary");
            }

            var choices = Add(_card, new VisualElement(), "chips", "choices");
            switch (_choices)
            {
                case "talk":
                    foreach (TopicEntry topic in world.Catalog.Topics)
                    {
                        Button b = Add(choices, Button("topic-" + topic.Id, topic.Name, () => _session.Talk(topic.Id)), "chip");
                        if (rel.KnownLikes.Contains(topic.Id)) b.AddToClassList("liked");
                        if (rel.KnownDislikes.Contains(topic.Id)) b.AddToClassList("disliked");
                    }
                    break;
                case "gift":
                    foreach (GiftEntry gift in world.Catalog.Gifts)
                        Add(choices, Button("gift-" + gift.Id, Capitalise(gift.Name), () => _session.Gift(gift.Id)), "chip");
                    break;
                case "date":
                    foreach (LocationEntry place in world.Catalog.Locations.Where(l => l.IsOpen(world.Now.Hour)))
                        Add(choices, Button("dateplace-" + place.Id, place.Name, () => _session.AskOnDate(place.Id)), "chip");
                    break;
            }
            if (_session.DateWith != null && _choices != "talk") choices.style.display = DisplayStyle.None;
        }

        /// <summary>During a date: the round, what you could do next, and goodnight.</summary>
        void DateCard(SimWorld world, Relationship rel)
        {
            DateScene date = _session.Date;
            string progress = date.Finished ? "The date's over." : $"Round {date.Round + 1} of {DateScene.Rounds}: what next?";
            Add(_card, new Label($"On a date at {date.Place.Name}. {progress}"), "date-progress");
            if (!date.Finished)
            {
                var options = Add(_card, new VisualElement(), "chips", "choices");
                for (int i = 0; i < date.Options.Count; i++)
                {
                    int index = i;
                    DateOption option = date.Options[i];
                    Button button = Add(options, Button("dateopt-" + i, option.Label, () => _session.DateChoose(index)), "chip");
                    if (option.Topic != null && rel.KnownLikes.Contains(option.Topic)) button.AddToClassList("liked");
                    if (option.Topic != null && rel.KnownDislikes.Contains(option.Topic)) button.AddToClassList("disliked");
                }
            }
            var actions = Add(_card, new VisualElement(), "actions");
            Add(actions, Button("act-end-date", "Say goodnight", () => _session.EndDate()), date.Finished ? "primary" : "chip");
        }

        void Meter(string label, float value, string kind)
        {
            var row = Add(_card, new VisualElement(), "meter");
            Add(row, new Label(label), "meter-label");
            var bar = Add(row, new VisualElement(), "meter-bar");
            var fill = Add(bar, new VisualElement(), "meter-fill", kind);
            fill.style.width = Length.Percent(Math.Max(0f, Math.Min(100f, value)));
            Add(row, new Label(value.ToString("0")), "meter-value");
        }

        Button Picker(string name, string text, string choices)
        {
            Button button = Button(name, text, () =>
            {
                _choices = _choices == choices ? null : choices;
                Refresh();
            });
            if (_choices == choices) button.AddToClassList("selected");
            return button;
        }

        /// <summary>Once you know someone a little, you can tell what looks they go for.</summary>
        const float StyleHintFamiliarity = 20f;

        static string StyleHint(ResidentProfile profile, SimWorld world)
        {
            List<string> styles = profile.StyleTaste.Where(t => t.Key != "date").OrderByDescending(t => t.Value).Take(2)
                .Select(t => (world.Characters.Archetype(t.Key)?.Name ?? t.Key).ToLowerInvariant()).ToList();
            return $"Goes for {string.Join(" and ", styles)} looks";
        }

        static string StageName(Stage stage) => stage switch
        {
            Stage.Stranger => "Stranger",
            Stage.Acquaintance => "Acquaintance",
            Stage.Friend => "Friend",
            Stage.Close => "Close friend",
            Stage.Dating => "Dating",
            _ => "Partner",
        };

        static string Capitalise(string text) => string.IsNullOrEmpty(text) ? text : char.ToUpperInvariant(text[0]) + text.Substring(1);

        static Button Button(string name, string text, Action clicked) => new Button(clicked) { name = name, text = text };

        static T Add<T>(VisualElement parent, T child, params string[] classes) where T : VisualElement
        {
            foreach (string c in classes) child.AddToClassList(c);
            parent.Add(child);
            return child;
        }
    }
}
