using System.Collections.Generic;
using System.Linq;
using Game.Sim.Relationships;
using Game.Sim.Residents;
using Game.Sim.Sandbox;
using NUnit.Framework;

namespace Game.Sim.Tests
{
    public class ChatTests
    {
        static readonly Personality Outgoing = new Personality { Extraversion = 0.95f, Neuroticism = 0.05f, Agreeableness = 0.3f, Openness = 0.7f };
        static readonly Personality Worrier = new Personality { Extraversion = 0.3f, Neuroticism = 0.9f, Agreeableness = 0.9f, Openness = 0.4f };

        static (SimWorld World, Interactions Act, Resident Resident) Familiar(Personality personality)
        {
            (SimWorld world, Resident resident) = Fixtures.OneResident(p => p.Personality = personality.Clone());
            world.Now = SimTime.At(1, 10);
            Relationship rel = world.RelationshipWith(resident);
            rel.Met = true;
            rel.Familiarity = 30f;
            rel.Affection = 20f;
            rel.LastSeenDay = 1;
            return (world, new Interactions(world), resident);
        }

        static ChatScene Scene(Resident resident, string opener) =>
            new ChatScene { With = resident, Opener = opener, Replies = ChatTaste.RepliesFor(opener, "music") };

        static int IndexOf(ChatScene chat, ChatStyle style) => chat.Replies.FindIndex(r => r.Style == style);

        static int Best(ChatScene chat) => chat.Replies.IndexOf(chat.Replies
            .OrderByDescending(r => ChatTaste.Preference(chat.With.Profile.Personality, r.Style, chat.Opener)).First());

        static int Worst(ChatScene chat) => chat.Replies.IndexOf(chat.Replies
            .OrderBy(r => ChatTaste.Preference(chat.With.Profile.Personality, r.Style, chat.Opener)).First());

        [Test]
        public void AGoodReplyDrawsThemOutAndTwoLeaveThemFeelingHeard()
        {
            (SimWorld world, Interactions act, Resident resident) = Familiar(Outgoing);
            act.StartChat(resident, out ChatScene chat);
            List<string> first = chat.Replies.Select(r => r.Text).ToList();
            InteractionResult more = act.ChatReply(chat, Best(chat));
            Assert.That(more.Reaction, Is.EqualTo("chat.react.good"));
            Assert.That(more.FollowUpReaction, Does.StartWith("chat.more." + chat.Opener + "."), "What they say next answers the style of the reply.");
            Assert.That(chat.Answered, Is.False);
            Assert.That(chat.Line, Is.EqualTo(more.FollowUp));
            Assert.That(chat.Replies.Select(r => r.Text), Is.Not.EqualTo(first), "New replies for what they said next.");

            InteractionResult heard = act.ChatReply(chat, Best(chat));
            Assert.That(heard.FollowUpReaction, Is.EqualTo("chat.close.heard"));
            Assert.That(chat.Answered);
            Assert.That(heard.Trust, Is.GreaterThan(more.Trust), "Feeling heard builds trust.");
            Assert.That(world.RelationshipWith(resident).ChatWins.Values.Sum(), Is.EqualTo(1), "One chat teaches one thing.");
        }

        [Test]
        public void AReplyThatMissesEndsTheChat()
        {
            (SimWorld _, Interactions act, Resident resident) = Familiar(Outgoing);
            act.StartChat(resident, out ChatScene chat);
            InteractionResult answer = act.ChatReply(chat, Worst(chat));
            Assert.That(answer.Reaction, Is.EqualTo("chat.react.bad"));
            Assert.That(answer.FollowUp, Is.Null);
            Assert.That(chat.Answered);
        }

        [Test]
        public void WarmthComfortsAWorrierAndTeasingStings()
        {
            (SimWorld world, Interactions act, Resident resident) = Familiar(Worrier);
            float before = world.RelationshipWith(resident).Affection;
            InteractionResult warm = act.ChatReply(Scene(resident, "worry"), IndexOf(Scene(resident, "worry"), ChatStyle.Warm));
            Assert.That(warm.Reaction, Is.EqualTo("chat.react.good"));
            Assert.That(world.RelationshipWith(resident).Affection, Is.GreaterThan(before));

            float mid = world.RelationshipWith(resident).Affection;
            InteractionResult tease = act.ChatReply(Scene(resident, "worry"), IndexOf(Scene(resident, "worry"), ChatStyle.Playful));
            Assert.That(tease.Reaction, Is.EqualTo("chat.react.bad"));
            Assert.That(world.RelationshipWith(resident).Affection, Is.LessThan(mid));
        }

        [Test]
        public void TeasingSuitsTheOutgoing()
        {
            (SimWorld _, Interactions act, Resident resident) = Familiar(Outgoing);
            Assert.That(ChatTaste.Favourite(Outgoing), Is.EqualTo(ChatStyle.Playful));
            Assert.That(act.ChatReply(Scene(resident, "day"), IndexOf(Scene(resident, "day"), ChatStyle.Playful)).Reaction, Is.EqualTo("chat.react.good"));
            Assert.That(act.ChatReply(Scene(resident, "day"), IndexOf(Scene(resident, "day"), ChatStyle.Warm)).Reaction, Is.EqualTo("chat.react.bad"));
        }

        [Test]
        public void StrangersDontShareWorries()
        {
            (SimWorld world, Resident resident) = Fixtures.OneResident(p => p.Personality = new Personality { Neuroticism = 1f, Openness = 0f });
            world.Now = SimTime.At(1, 10);
            var act = new Interactions(world);
            for (int i = 0; i < 30; i++)
            {
                world.RelationshipWith(resident).Familiarity = 5f;
                act.StartChat(resident, out ChatScene chat);
                Assert.That(chat.Opener, Is.Not.EqualTo("worry"));
            }
        }

        [Test]
        public void OpenersFitWhoTheyAre()
        {
            (SimWorld world, Interactions act, Resident resident) = Familiar(new Personality { Neuroticism = 0f, Openness = 0f });
            (SimWorld anxiousWorld, Interactions anxiousAct, Resident anxious) = Familiar(new Personality { Neuroticism = 1f, Openness = 0.2f });
            int calmWorries = 0, anxiousWorries = 0;
            for (int i = 0; i < 40; i++)
            {
                world.RelationshipWith(resident).Familiarity = 30f;
                anxiousWorld.RelationshipWith(anxious).Familiarity = 30f;
                act.StartChat(resident, out ChatScene calm);
                anxiousAct.StartChat(anxious, out ChatScene worried);
                Assert.That(calm.Opener, Is.Not.EqualTo("dream"), "Someone with no openness doesn't daydream aloud.");
                if (calm.Opener == "worry") calmWorries++;
                if (worried.Opener == "worry") anxiousWorries++;
            }
            Assert.That(calmWorries, Is.Zero);
            Assert.That(anxiousWorries, Is.GreaterThan(3));
        }

        [Test]
        public void AnInterestOpenerRevealsALove()
        {
            (SimWorld world, Interactions act, Resident resident) = Familiar(Outgoing);
            for (int i = 0; i < 20; i++)
            {
                InteractionResult opened = act.StartChat(resident, out ChatScene chat);
                Assert.That(chat.Replies.Select(r => r.Style), Is.EquivalentTo(new[] { ChatStyle.Warm, ChatStyle.Playful, ChatStyle.Curious }));
                Assert.That(opened.Line, Is.Not.Empty);
                if (chat.Opener != "interest") continue;
                Assert.That(world.RelationshipWith(resident).KnownLikes, Does.Contain(chat.Topic));
                Assert.That(resident.Profile.Interest(chat.Topic), Is.GreaterThanOrEqualTo(ProfileGenerator.LikeThreshold));
                Assert.That(opened.Line, Does.Contain(world.Catalog.Topic(chat.Topic).Name).IgnoreCase);
                return;
            }
            Assert.Fail("No interest opener in 20 chats.");
        }

        [Test]
        public void WhatTheyRespondToIsLearntAfterItWorksTwice()
        {
            (SimWorld world, Interactions act, Resident resident) = Familiar(Outgoing);
            Relationship rel = world.RelationshipWith(resident);
            string learnt = null;
            for (int i = 0; i < 10 && rel.KnownChatStyle == null; i++)
            {
                act.StartChat(resident, out ChatScene chat);
                ChatReply best = chat.Replies.OrderByDescending(r => ChatTaste.Preference(resident.Profile.Personality, r.Style, chat.Opener)).First();
                InteractionResult answer = act.ChatReply(chat, chat.Replies.IndexOf(best));
                Assert.That(answer.Reaction, Is.EqualTo("chat.react.good"));
                if (rel.KnownChatStyle != null) learnt = answer.Learnt;
            }
            Assert.That(rel.KnownChatStyle, Is.Not.Null);
            Assert.That(rel.ChatWins[rel.KnownChatStyle], Is.EqualTo(2));
            Assert.That(learnt, Does.Contain("responds well to"));

            SimWorld loaded = SimSave.FromJson(SimSave.Capture(world).ToJson()).Restore(Fixtures.Sim(), Fixtures.Characters());
            Assert.That(loaded.RelationshipWith(loaded.Residents[0]).KnownChatStyle, Is.EqualTo(rel.KnownChatStyle), "It's remembered.");
        }

        [Test]
        public void TheSessionHoldsTheChatUntilItsAnswered()
        {
            SimWorld world = Fixtures.Town(0x5EED, 24);
            world.Now = SimTime.At(5, 12);
            var session = new TownSession(world);
            session.Travel(world.Catalog.Locations.Where(l => session.IsOpen(l.Id)).OrderByDescending(l => world.At(l.Id).Count()).First().Id);
            Resident someone = session.Present().First();
            session.Select(someone);

            Assert.That(session.StartChat(), Is.Not.Null);
            Assert.That(session.Chat, Is.Not.Null);
            Assert.That(session.Chat.With, Is.EqualTo(someone));
            for (int beat = 0; beat < ChatScene.Beats && session.Chat != null; beat++)
                Assert.That(session.ChatReply(Best(session.Chat)), Is.Not.Null);
            Assert.That(session.Chat, Is.Null, "Two beats at most.");

            Assert.That(session.ChatReply(0), Is.Null);
            Assert.That(session.Notice, Does.Contain("no conversation"));

            session.StartChat();
            session.Compliment();
            Assert.That(session.Chat, Is.Null, "Doing something else moves the conversation on.");
        }
    }
}
