using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Feedback
{
    /// <summary>The last messages the game logged since it started, with the stacks of errors, for feedback tickets.</summary>
    public static class FeedbackLog
    {
        public const int Capacity = 200;
        const int MaxMessage = 2000, MaxStack = 4000;

        public struct Entry
        {
            public string Time;
            public string Type;
            public string Message;
            public string Stack;
        }

        static readonly Queue<Entry> Entries = new Queue<Entry>();
        static readonly object Lock = new object();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Listen()
        {
            lock (Lock) Entries.Clear();
            Application.logMessageReceivedThreaded -= Record;
            Application.logMessageReceivedThreaded += Record;
        }

        public static void Record(string message, string stack, LogType type)
        {
            bool error = type == LogType.Error || type == LogType.Exception || type == LogType.Assert;
            var entry = new Entry
            {
                Time = DateTime.Now.ToString("HH:mm:ss.fff"),
                Type = type.ToString(),
                Message = Clip(message, MaxMessage),
                Stack = error ? Clip(stack, MaxStack) : null,
            };
            lock (Lock)
            {
                Entries.Enqueue(entry);
                while (Entries.Count > Capacity) Entries.Dequeue();
            }
        }

        public static List<Entry> Recent()
        {
            lock (Lock) return new List<Entry>(Entries);
        }

        static string Clip(string text, int max) => text == null || text.Length <= max ? text : text.Substring(0, max) + "…";
    }
}
