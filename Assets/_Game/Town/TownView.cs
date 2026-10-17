using System;
using System.Collections.Generic;
using System.Linq;
using Game.Sim;
using Game.Sim.Relationships;
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
        readonly Label _clock, _place, _notice, _speaker, _line, _followUp, _learnt;
        readonly VisualElement _dialogue, _deltas, _places, _here, _card;
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
            Add(top, new VisualElement(), "spacer");
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
            Add(panel, new Label("Here"), "section");
            _here = Add(panel, new VisualElement(), "here");
            _card = Add(panel, new VisualElement(), "card");

            session.Changed += Refresh;
            Refresh();
        }

        public void Refresh()
        {
            SimWorld world = _session.World;
            _clock.text = world.Now.ToString();
            _place.text = _session.LocationName(_session.Location) + (_session.DateWith != null ? $"  ·  on a date with {_session.DateWith.Profile.FirstName}" : "");
            _notice.text = _session.Notice ?? "";
            _notice.style.display = _session.Notice == null ? DisplayStyle.None : DisplayStyle.Flex;

            RefreshDialogue();
            RefreshPlaces(world);
            RefreshHere();
            RefreshCard(world);
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

            Meter("Affection", rel.Affection, "affection");
            Meter("Attraction", rel.Attraction, "attraction");
            Meter("Familiarity", rel.Familiarity, "familiarity");
            Meter("Trust", rel.Trust, "trust");

            string Topics(IEnumerable<string> ids) => string.Join(", ", ids.Select(id => world.Catalog.Topic(id)?.Name ?? id));
            if (rel.KnownLikes.Count + rel.KnownDislikes.Count == 0)
                Add(_card, new Label("Talk to find out what they're into."), "hint");
            if (rel.KnownLikes.Count > 0) Add(_card, new Label("Loves " + Topics(rel.KnownLikes)), "known", "liked");
            if (rel.KnownDislikes.Count > 0) Add(_card, new Label("Not into " + Topics(rel.KnownDislikes)), "known", "disliked");

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
