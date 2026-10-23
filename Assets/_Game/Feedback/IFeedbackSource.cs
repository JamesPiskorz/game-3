using System;
using System.Collections.Generic;

namespace Game.Feedback
{
    /// <summary>
    /// A screen that knows the game state worth putting in a feedback ticket: the town's day, place and people, the
    /// creator's look. <see cref="FeedbackReporter"/> asks every active one when a ticket is made.
    /// </summary>
    public interface IFeedbackSource
    {
        /// <summary>The section's name in the ticket's context.json ("town", "creator").</summary>
        string FeedbackSection { get; }

        /// <summary>The state needed to reproduce what the player saw: plain objects, lists and dictionaries, written as JSON.</summary>
        object DescribeForFeedback();

        /// <summary>Rows for the issue's summary table (the game time, the place).</summary>
        IEnumerable<(string Label, string Value)> FeedbackSummary => Array.Empty<(string, string)>();

        /// <summary>Who the characters on stage are, by their DNA seed (the player, residents), so the ticket says who's who.</summary>
        IReadOnlyDictionary<ulong, string> FeedbackNames => null;

        /// <summary>Files to attach, by name: a save that reproduces the state.</summary>
        IReadOnlyDictionary<string, string> FeedbackAttachments => null;
    }
}
