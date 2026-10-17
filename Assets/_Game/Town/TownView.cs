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
        readonly VisualElement _root;
        VisualElement _bubbleLayer;
        readonly Dictionary<int, Label> _bubbles = new Dictionary<int, Label>();
        readonly Label _clock, _place, _coins, _notice, _hint, _speaker, _line, _followUp, _learnt;
        readonly VisualElement _dialogue, _deltas, _places, _here, _card, _clothes;
        readonly Button _work, _contactsButton, _messagesButton;
        bool _showMessages;
        readonly Label _hereTitle, _stats;
        readonly VisualElement _contacts;
        readonly ScrollView _panel;
        bool _showContacts;
        string _choices; // "talk", "gift" or "date": which picker is open under the actions
        Resident _choicesFor;

        public TownView(VisualElement root, TownSession session, Action save, Action load, Action menu = null)
        {
            _session = session;
            root.AddToClassList("town");
            _root = root;

            var main = Add(root, new VisualElement(), "main");
            var top = Add(main, new VisualElement(), "topbar");
            _clock = Add(top, new Label(), "clock");
            _place = Add(top, new Label(), "place");
            _coins = Add(top, new Label(), "coins");
            Add(top, new VisualElement(), "spacer");
            _work = Add(top, Button("work", "Work a shift", () => _session.Work()));
            _messagesButton = Add(top, Button("messages", "Messages", () =>
            {
                _showMessages = !_showMessages;
                _showContacts = false;
                Refresh();
            }));
            _contactsButton = Add(top, Button("contacts", "Contacts", () =>
            {
                _showContacts = !_showContacts;
                _showMessages = false;
                Refresh();
            }));
            Add(top, Button("wait", "Wait an hour", () => _session.Wait()));
            Add(top, Button("sleep", "Sleep", () => _session.Sleep()));
            Add(top, Button("save", "Save", save));
            Add(top, Button("load", "Load", load));
            if (menu != null) Add(top, Button("menu", "Menu", menu));
            _notice = Add(main, new Label(), "notice");
            _hint = Add(main, new Label(), "hint-bar");
            Add(main, new VisualElement(), "spacer");
            _dialogue = Add(main, new VisualElement(), "dialogue");
            _speaker = Add(_dialogue, new Label(), "speaker");
            _line = Add(_dialogue, new Label(), "line");
            _followUp = Add(_dialogue, new Label(), "line", "follow-up");
            _deltas = Add(_dialogue, new VisualElement(), "deltas");
            _learnt = Add(_dialogue, new Label(), "learnt");

            var panel = _panel = Add(root, new ScrollView(), "panel");
            _stats = Add(panel, new Label(), "stats");
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
            _clock.text = world.Now + "   \u00B7   " + Forecast.Describe(world.Weather);
            _place.text = _session.LocationName(_session.Location) + (_session.DateWith != null ? $"  ·  on a date with {_session.DateWith.Profile.FirstName}" : "");
            _coins.text = $"{world.Player.Money} coins";
            _stats.text = "You  ·  " + string.Join("  ·  ", SimCatalog.Stats.Select(stat => $"{Capitalise(stat)} {world.Player.Stat(stat):0}"));
            _work.style.display = _session.CanWork ? DisplayStyle.Flex : DisplayStyle.None;
            _notice.text = _session.Notice ?? "";
            _notice.style.display = _session.Notice == null ? DisplayStyle.None : DisplayStyle.Flex;
            string hint = _session.Hint();
            _hint.text = hint ?? "";
            _hint.style.display = hint == null ? DisplayStyle.None : DisplayStyle.Flex;

            RefreshDialogue();
            RefreshBubbles();
            RefreshPlaces(world);
            RefreshContacts();
            if (_showMessages) RefreshMessages();
            int pending = _session.Messages().Count(m => m.Pending);
            _messagesButton.text = pending > 0 ? $"Messages ({pending})" : "Messages";
            _messagesButton.EnableInClassList("selected", _showMessages);
            bool overlay = _showContacts || _showMessages;
            DisplayStyle normal = overlay ? DisplayStyle.None : DisplayStyle.Flex;
            _hereTitle.style.display = _here.style.display = _clothes.style.display = normal;
            if (overlay) _card.style.display = DisplayStyle.None;
            if (overlay) return;
            RefreshHere();
            RefreshClothes(world);
            RefreshCard(world);
        }

        /// <summary>The side panel's width plus its margins (see Town.uss), which bubbles stay clear of.</summary>
        const float PanelWidth = 500f;

        /// <summary>Speech bubbles for what people nearby are saying out loud this hour.</summary>
        void RefreshBubbles()
        {
            if (_bubbleLayer == null)
            {
                _bubbleLayer = new VisualElement { pickingMode = PickingMode.Ignore };
                _bubbleLayer.AddToClassList("bubble-layer");
                _root.Add(_bubbleLayer);
            }
            foreach (Label bubble in _bubbles.Values) bubble.RemoveFromHierarchy();
            _bubbles.Clear();
            foreach ((Resident resident, string line) in _session.Overheard())
            {
                var bubble = new Label(line) { pickingMode = PickingMode.Ignore };
                bubble.AddToClassList("bubble");
                bubble.style.display = DisplayStyle.None; // until placed over a head
                _bubbleLayer.Add(bubble);
                _bubbles[resident.Index] = bubble;
            }
        }

        /// <summary>Keeps each bubble over its speaker's head; called every frame, since people and the camera move.</summary>
        public void PlaceBubbles(Func<int, UnityEngine.Vector3?> head, UnityEngine.Camera camera)
        {
            if (_bubbleLayer?.panel == null) return;
            foreach (KeyValuePair<int, Label> pair in _bubbles)
            {
                UnityEngine.Vector3? world = head(pair.Key);
                UnityEngine.Vector3 viewport = world.HasValue ? camera.WorldToViewportPoint(world.Value) : default;
                bool visible = world.HasValue && viewport.z > 0f;
                pair.Value.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
                if (!visible) continue;
                // Viewport to panel by proportion: right whether the camera draws to the screen or to a texture.
                UnityEngine.Rect area = _bubbleLayer.layout;
                // Keep the whole bubble in the open part of the screen, left of the side panel.
                float half = float.IsNaN(pair.Value.resolvedStyle.width) ? 120f : pair.Value.resolvedStyle.width / 2f;
                float right = area.width - PanelWidth - half;
                pair.Value.style.left = UnityEngine.Mathf.Clamp(viewport.x * area.width, half + 12f, UnityEngine.Mathf.Max(half + 12f, right));
                pair.Value.style.top = (1f - viewport.y) * area.height;
            }
        }

        /// <summary>Invitations from residents, newest first; a pending one can be answered by going there.</summary>
        void RefreshMessages()
        {
            _contacts.Clear();
            _contacts.style.display = DisplayStyle.Flex;
            Add(_contacts, new Label("Messages"), "section");
            List<Invitation> messages = _session.Messages();
            if (messages.Count == 0) Add(_contacts, new Label("No messages yet. Get close to someone and they'll start inviting you out."), "hint");
            for (int i = 0; i < messages.Count; i++)
            {
                Invitation message = messages[i];
                Resident from = _session.World.Residents[message.From];
                var row = Add(_contacts, new VisualElement(), "contact");
                string status = message.Kept ? "you met up" : message.Missed ? "missed" : $"day {message.Day + 1}, {message.Hour}:00 at {_session.LocationName(message.Location)}";
                Button button = Add(row, Button("message-" + i, $"{from.Profile.FirstName}   \u00B7   {status}", () =>
                {
                    _showMessages = false;
                    _session.Answer(message);
                }), "person");
                button.SetEnabled(message.Pending && _session.DateWith == null);
                Add(row, new Label("\u201C" + message.Message + "\u201D"), "hint", "contact-hint");
            }
        }

        /// <summary>Everyone you've met, closest first, with what you know about where they are.</summary>
        void RefreshContacts()
        {
            _contactsButton.EnableInClassList("selected", _showContacts);
            if (_showMessages) return;
            _contacts.Clear();
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

        /// <summary>The boutique's stock at the mall, the wardrobe at home, and things to do wherever the player is.</summary>
        void RefreshClothes(SimWorld world)
        {
            _clothes.Clear();
            List<ActivityEntry> activities = _session.Activities();
            if (activities.Count > 0)
            {
                Add(_clothes, new Label("Things to do"), "section");
                var row = Add(_clothes, new VisualElement(), "chips");
                foreach (ActivityEntry activity in activities)
                {
                    float gain = TownSession.Gain(activity, world.Player.Stat(activity.Stat));
                    string cost = activity.Cost > 0 ? $", {activity.Cost} coins" : "";
                    Button b = Add(row, Button("activity-" + activity.Id, $"{activity.Name}  ·  +{gain:0} {activity.Stat}  ({activity.Hours}h{cost})",
                        () => _session.DoActivity(activity.Id)), "chip");
                    b.SetEnabled(world.Player.Money >= activity.Cost);
                }
            }
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
            if (last.Impression is float impression)
            {
                string verdict = impression >= 0.6f ? "great" : impression >= 0.2f ? "good" : impression >= -0.2f ? "okay" : "lukewarm";
                Add(_deltas, new Label("First impression: " + verdict), "delta", impression >= 0.2f ? "up" : "down");
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
            if (rel.Familiarity >= LifeFamiliarity || rel.KnowsTheirLife) Add(_card, new Label(Life(selected, world)), "known", "life");

            Meter("Affection", rel.Affection, "affection");
            Meter("Attraction", rel.Attraction, "attraction");
            Meter("Familiarity", rel.Familiarity, "familiarity");
            Meter("Trust", rel.Trust, "trust");

            string Topics(IEnumerable<string> ids) => string.Join(", ", ids.Select(id => world.Catalog.Topic(id)?.Name ?? id));
            if (rel.KnownLikes.Count + rel.KnownDislikes.Count == 0)
                Add(_card, new Label("Talk to find out what they're into."), "hint");
            if (rel.KnownLikes.Count > 0) Add(_card, new Label("Loves " + Topics(rel.KnownLikes)), "known", "liked");
            if (rel.KnownDislikes.Count > 0) Add(_card, new Label("Not into " + Topics(rel.KnownDislikes)), "known", "disliked");
            if (rel.KnownChatStyle != null && Enum.TryParse(rel.KnownChatStyle, out ChatStyle knownStyle))
                Add(_card, new Label("Responds well to " + Interactions.StyleWords(knownStyle)), "known", "liked");

            if (_session.Date != null)
            {
                DateCard(world, rel);
                return;
            }

            ChatScene chat = _session.Chat;
            if (chat != null && chat.With == selected)
            {
                // They've said something and are waiting for an answer: the replies stand in for the actions until then.
                // Once you know what they respond to, that reply is marked.
                var replies = Add(_card, new VisualElement(), "chat-replies");
                Add(replies, new Label("Your reply:"), "chat-prompt");
                Button last = null;
                for (int i = 0; i < chat.Replies.Count; i++)
                {
                    int index = i;
                    last = Add(replies, Button("chatopt-" + i, chat.Replies[i].Text, () => _session.ChatReply(index)), "chip", "chat-reply");
                    if (rel.KnownChatStyle == chat.Replies[i].Style.ToString()) last.AddToClassList("liked");
                }
                Button shown = last;
                _panel.schedule.Execute(() => { if (shown.panel != null) _panel.ScrollTo(shown); });
                return;
            }

            var actions = Add(_card, new VisualElement(), "actions");
            if (_session.DateWith == null) Add(actions, Button("act-greet", "Say hi", () => _session.Greet()));
            Add(actions, Picker("act-talk", "Talk about…", "talk"));
            Add(actions, Picker("act-ask", "Ask about…", "ask"));
            if (_session.DateWith == null) Add(actions, Button("act-chat", "Chat", () =>
            {
                _choices = null;
                _session.StartChat();
            }));
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
                case "ask":
                    foreach ((string id, string label) in new[] { ("likes", "What they're into"), ("plans", "Their plans today"), ("friends", "Their friends") })
                        Add(choices, Button("ask-" + id, label, () => _session.Ask(id)), "chip");
                    break;
                case "gift":
                    foreach (GiftEntry gift in world.Catalog.Gifts)
                    {
                        Button b = Add(choices, Button("gift-" + gift.Id, $"{Capitalise(gift.Name)}  ·  {gift.Price}", () => _session.Gift(gift.Id)), "chip");
                        b.SetEnabled(world.Player.Money >= gift.Price);
                    }
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

        /// <summary>Knowing someone a bit better, you hear about their friends and whether they're seeing anyone.</summary>
        const float LifeFamiliarity = 25f;

        static string Life(Resident resident, SimWorld world)
        {
            var parts = new List<string>();
            List<string> siblings = world.SiblingsOf(resident).Select(s => s.Profile.FirstName).ToList();
            if (siblings.Count > 0) parts.Add($"{string.Join(" and ", siblings)}'s {resident.Profile.SiblingWord}");
            List<string> friends = world.Social.FriendsOf(resident.Index).Select(i => world.Residents[i].Profile.FirstName).ToList();
            if (friends.Count > 0) parts.Add("friends with " + (friends.Count == 1 ? friends[0] : string.Join(", ", friends.Take(friends.Count - 1)) + " and " + friends.Last()));
            int partner = world.Social.PartnerOf(resident.Index);
            parts.Add(partner >= 0 ? $"seeing {world.Residents[partner].Profile.FirstName}" : "single");
            string text = string.Join("; ", parts);
            return char.ToUpperInvariant(text[0]) + text.Substring(1);
        }

        /// <summary>Once you know someone a little, you can tell what looks they go for.</summary>
        const float StyleHintFamiliarity = 20f;

        static string StyleHint(ResidentProfile profile, SimWorld world)
        {
            List<string> styles = profile.StyleTaste.Where(t => t.Key != "date").OrderByDescending(t => t.Value).Take(2)
                .Select(t => (world.Characters.Archetype(t.Key)?.Name ?? t.Key).ToLowerInvariant()).ToList();
            string hint = $"Goes for {string.Join(" and ", styles)} looks";
            world.Catalog.Archetypes.TryGetValue(profile.Archetype ?? "", out ArchetypeTendencies tendencies);
            string admired = tendencies?.Admires.OrderByDescending(a => a.Value).Select(a => a.Key).FirstOrDefault();
            return admired == null ? hint : $"{hint}; admires {admired}";
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
