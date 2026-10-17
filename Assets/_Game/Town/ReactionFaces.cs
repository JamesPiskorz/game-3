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
                case "gift.ok": return ("happy", 0.6f);
                case "gift.dislike": return ("sad", 0.7f);
                case "gift.awkward": return ("surprised", 0.7f);
                case "gift.again": return ("shy", 0.7f);
                case "date.yes": return shy ? ("shy", 1f) : ("delighted", 1f);
                case "date.no": return ("sad", 0.6f);
                case "date.notyet": return ("shy", 0.6f);
                case "date.great": return ("delighted", 1f);
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
                case "greet.stranger.impressed": return shy ? ("shy", 0.9f) : ("delighted", 0.7f);
                case "compliment.style": return shy ? ("shy", 1f) : ("happy", 1f);
                case "date.taken": return ("shy", 0.5f);
                case "date.leaves": return shy ? ("shy", 1f) : ("delighted", 0.8f);
                default: return ("neutral", 0f);
            }
        }
    }
}
