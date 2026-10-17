using Game.Sim;

namespace Game.Town
{
    /// <summary>
    /// Which face a resident pulls for each reaction, flavoured by their tone: shy residents go bashful where others
    /// beam, dry ones show less.
    /// </summary>
    public static class ReactionFaces
    {
        public static (string Expression, float Strength) For(string reaction, string tone)
        {
            bool shy = tone == Tones.Shy, dry = tone == Tones.Dry;
            // A follow-up that answers the player's style of reply looks like the follow-up it varies.
            if (reaction != null && reaction.StartsWith("chat.more.") && reaction.Split('.').Length == 4)
                reaction = reaction.Substring(0, reaction.LastIndexOf('.'));
            switch (reaction)
            {
                case "greet.stranger": return shy ? ("shy", 0.8f) : dry ? ("happy", 0.25f) : ("happy", 0.7f);
                case "greet.acquaintance":
                case "greet.friend": return ("happy", dry ? 0.4f : 0.8f);
                case "greet.close":
                case "greet.dating": return shy ? ("shy", 1f) : ("delighted", 0.8f);
                case "talk.love": return dry ? ("happy", 0.8f) : ("delighted", 1f);
                case "talk.neutral": return ("happy", 0.4f);
                case "talk.dislike": return dry ? ("annoyed", 0.9f) : ("sad", 0.6f);
                case "talk.bored": return ("annoyed", 0.6f);
                case "compliment.pleased": return shy ? ("shy", 1f) : ("happy", 1f);
                case "compliment.awkward": return ("surprised", 0.5f);
                case "gift.love": return ("delighted", 1f);
                case "greet.confess":
                case "greet.confess.taken": return ("shy", 1f);
                case "confess.yes": return shy ? ("shy", 1f) : ("delighted", 1f);
                case "confess.yes.leaves": return ("shy", 0.7f);
                case "confess.no": return ("sad", 0.7f);
                case "gift.clothes.love": return shy ? ("shy", 1f) : ("delighted", 1f);
                case "gift.clothes.ok": return ("happy", 0.6f);
                case "gift.clothes.meh": return dry ? ("annoyed", 0.5f) : ("surprised", 0.5f);
                case "gift.ok": return ("happy", 0.6f);
                case "gift.dislike": return ("sad", 0.7f);
                case "gift.awkward": return ("surprised", 0.7f);
                case "gift.again": return ("shy", 0.7f);
                case "date.yes": return shy ? ("shy", 1f) : ("delighted", 1f);
                case "date.no": return ("sad", 0.6f);
                case "date.notyet": return ("shy", 0.6f);
                case "date.great": return ("delighted", 1f);
                case "date.partner": return shy ? ("shy", 1f) : ("delighted", 1f);
                case "date.activity.love": return dry ? ("happy", 0.9f) : ("delighted", 1f);
                case "date.activity.ok": return ("happy", 0.5f);
                case "date.activity.dislike": return ("annoyed", 0.5f);
                case "date.fine": return ("happy", 0.7f);
                case "date.awkward": return ("sad", 0.5f);
                case "date.talk.love": return dry ? ("happy", 0.9f) : ("delighted", 1f);
                case "date.talk.neutral": return ("happy", 0.4f);
                case "date.talk.dislike": return ("annoyed", 0.6f);
                case "date.repeat": return ("surprised", 0.4f);
                case "date.compliment": return shy ? ("shy", 1f) : ("happy", 0.9f);
                case "date.compliment.again": return ("annoyed", 0.3f);
                case "greet.gossip": return shy ? ("shy", 0.8f) : ("happy", 0.8f);
                case "greet.jealous": return dry ? ("annoyed", 0.9f) : ("sad", 0.8f);
                case "greet.news.together": return ("surprised", 0.6f);
                case "greet.news.apart": return ("sad", 0.5f);
                case "greet.news.mine.together": return shy ? ("shy", 1f) : ("delighted", 0.9f);
                case "greet.news.mine.apart": return ("sad", 0.8f);
                case "greet.remember.date":
                case "greet.remember.gift":
                case "greet.remember.topic": return shy ? ("shy", 0.8f) : ("happy", 0.9f);
                case "greet.cheated": return dry ? ("annoyed", 1f) : ("sad", 0.9f);
                case "greet.breakup": return dry ? ("annoyed", 0.8f) : ("sad", 1f);
                case "greet.player.makeover.love": return shy ? ("shy", 1f) : ("delighted", 1f);
                case "greet.player.makeup.love": return shy ? ("shy", 1f) : ("delighted", 0.9f);
                case "greet.player.makeup.nice": return ("happy", 0.6f);
                case "greet.player.makeup.meh": return dry ? ("annoyed", 0.4f) : ("surprised", 0.5f);
                case "greet.player.makeover.nice": return ("happy", 0.6f);
                case "greet.player.makeover.meh": return dry ? ("annoyed", 0.4f) : ("surprised", 0.6f);
                case "greet.makeover.haircut":
                case "greet.makeover.colour":
                case "greet.makeover.clothes":
                case "greet.makeover.makeup": return shy ? ("shy", 0.9f) : dry ? ("happy", 0.4f) : ("delighted", 0.8f);
                case "compliment.makeover.haircut":
                case "compliment.makeover.colour":
                case "compliment.makeover.clothes":
                case "compliment.makeover.makeup": return shy ? ("shy", 1f) : ("delighted", 1f);
                case "greet.stranger.family": return shy ? ("shy", 1f) : ("delighted", 0.8f);
                case "greet.stranger.impressed.mark":
                case "greet.stranger.impressed": return shy ? ("shy", 0.9f) : ("delighted", 0.7f);
                case "compliment.style": return shy ? ("shy", 1f) : ("happy", 1f);
                case "ask.likes":
                case "ask.plans":
                case "ask.friends": return shy ? ("shy", 0.6f) : ("happy", 0.6f);
                case "ask.friends.partner": return ("happy", 0.5f);
                case "ask.friends.none": return ("sad", 0.4f);
                case "ask.nosy":
                case "ask.again": return ("annoyed", 0.5f);
                case "invite.met": return shy ? ("shy", 1f) : ("delighted", 0.9f);
                case "chat.open.interest": return dry ? ("happy", 0.6f) : ("delighted", 0.8f);
                case "chat.open.day": return ("happy", 0.3f);
                case "chat.open.worry": return ("sad", 0.6f);
                case "chat.open.dream": return shy ? ("shy", 0.6f) : ("happy", 0.7f);
                case "chat.open.looks": return shy ? ("shy", 0.8f) : ("happy", 0.7f);
                case "chat.react.good": return shy ? ("shy", 1f) : ("delighted", 0.9f);
                case "chat.react.ok": return ("happy", 0.4f);
                case "chat.react.bad": return dry ? ("annoyed", 0.8f) : ("sad", 0.6f);
                case "chat.more.interest": return dry ? ("happy", 0.7f) : ("delighted", 0.9f);
                case "chat.more.day": return shy ? ("shy", 0.8f) : ("happy", 0.6f);
                case "chat.more.worry": return shy ? ("shy", 0.7f) : ("sad", 0.5f);
                case "chat.more.dream": return shy ? ("shy", 0.7f) : ("happy", 0.8f);
                case "chat.more.looks": return shy ? ("shy", 0.9f) : ("delighted", 0.7f);
                case "chat.close.heard": return shy ? ("shy", 1f) : ("delighted", 1f);
                case "date.taken": return ("shy", 0.5f);
                case "date.leaves": return shy ? ("shy", 1f) : ("delighted", 0.8f);
                default: return ("neutral", 0f);
            }
        }
    }
}
