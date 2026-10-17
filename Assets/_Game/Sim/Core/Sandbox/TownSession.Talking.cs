using System;
using System.Collections.Generic;
using System.Linq;
using Game.Characters.Content;
using Game.Characters.Dna;
using Game.Characters.Generation;
using Game.Sim.Relationships;
using Game.Sim.Residents;

namespace Game.Sim.Sandbox
{
    /// <summary>Turning to someone and talking: hello, topics, compliments, questions, chats and gifts.</summary>
    public sealed partial class TownSession
    {
        public void Select(Resident resident)
        {
            Notice = null;
            if (resident != Selected) Chat = null;
            Selected = resident != null && Present().Contains(resident) ? resident : null;
            Raise();
        }

        public InteractionResult Greet() => Do(r => Act.Greet(r));
        public InteractionResult Talk(string topic) => Do(r => Act.Talk(r, topic));
        public InteractionResult Compliment() => Do(r => Act.Compliment(r));
        public InteractionResult Ask(string question) => Do(r => Act.Ask(r, question));

        /// <summary>The chat the selected resident started, waiting for the player's reply (or null).</summary>
        public ChatScene Chat { get; private set; }

        /// <summary>Lets the selected resident start a conversation; reply with <see cref="ChatReply"/>.</summary>
        public InteractionResult StartChat()
        {
            ChatScene chat = null;
            InteractionResult opened = Do(r => Act.StartChat(r, out chat), chatting: true);
            if (opened != null && Selected != null)
            {
                Chat = chat;
                Raise();
            }
            return opened;
        }

        public InteractionResult ChatReply(int reply)
        {
            Notice = null;
            if (Chat == null || Chat.Answered || reply < 0 || reply >= Chat.Replies.Count || Selected != Chat.With)
            {
                Chat = null;
                Notice = "There's no conversation to answer.";
                Raise();
                return null;
            }
            ChatScene chat = Chat;
            InteractionResult answer = Do(r => Act.ChatReply(chat, reply), chatting: true);
            Chat = chat.Answered || Selected != chat.With ? null : chat; // a reply that lands draws them out, and the chat goes on
            Raise();
            return answer;
        }
        /// <summary>Gives a gift, which costs its price in coins.</summary>
        public InteractionResult Gift(string giftId)
        {
            GiftEntry gift = World.Catalog.Gift(giftId);
            if (gift != null && Selected != null && World.Player.Money < gift.Price)
            {
                Notice = $"{Capitalise(gift.Name)} costs {gift.Price} coins, and you have {World.Player.Money}.";
                Raise();
                return null;
            }
            InteractionResult result = Do(r => Act.Gift(r, giftId));
            if (result != null && gift != null)
            {
                World.Player.Money -= gift.Price;
                _today.Spent += gift.Price;
                Raise();
            }
            return result;
        }
    }
}
