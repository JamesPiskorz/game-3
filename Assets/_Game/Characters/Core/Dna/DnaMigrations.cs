using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Game.Characters.Dna
{
    /// <summary>
    /// Upgrades saved DNA JSON to the current version one step at a time. To change the DNA layout:
    /// bump <see cref="CurrentVersion"/>, add a step that rewrites version N JSON into version N + 1,
    /// and add a test that loads a real version N sample. Never edit an existing step; old saves depend on it.
    /// </summary>
    public static class DnaMigrations
    {
        public const int CurrentVersion = 1;

        // Keyed by the version a step upgrades from.
        static readonly Dictionary<int, Action<JObject>> Steps = new Dictionary<int, Action<JObject>>();

        public static void Upgrade(JObject json)
        {
            JToken versionToken = json["version"];
            if (versionToken == null || versionToken.Type != JTokenType.Integer)
                throw new FormatException("Character DNA has no integer \"version\" field.");

            int version = versionToken.Value<int>();
            if (version < 1 || version > CurrentVersion)
                throw new NotSupportedException($"Character DNA version {version} is not supported (this build reads versions 1 to {CurrentVersion}).");

            while (version < CurrentVersion)
            {
                Steps[version](json);
                version++;
                json["version"] = version;
            }
        }
    }
}
