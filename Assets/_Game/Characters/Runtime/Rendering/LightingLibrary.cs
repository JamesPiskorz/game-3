using System.Collections.Generic;
using UnityEngine;

namespace Game.Characters.Rendering
{
    /// <summary>The presets the game uses, found by name.</summary>
    [CreateAssetMenu(menuName = "Game/Rendering/Lighting Library")]
    public sealed class LightingLibrary : ScriptableObject
    {
        public const string Day = "day", Sunset = "sunset", Night = "night", Interior = "interior", Studio = "studio";

        public List<LightingPreset> Presets = new List<LightingPreset>();

        public LightingPreset Get(string id)
        {
            foreach (LightingPreset preset in Presets)
                if (preset != null && preset.name == id) return preset;
            return null;
        }

        /// <summary>
        /// The outdoor look at a minute of the day: day 7:00-17:00, sunset to 20:00, night otherwise, each change blended
        /// over half an hour centred on the hour (so a scene at 16:55 is mostly day, at 17:05 mostly sunset).
        /// </summary>
        public LightingLook Outdoors(int minuteOfDay)
        {
            LightingLook day = Get(Day).Look, sunset = Get(Sunset).Look, night = Get(Night).Look;
            float m = minuteOfDay;
            if (m < 7 * 60 - Blend || m >= 20 * 60 + Blend) return night;
            if (m < 7 * 60 + Blend) return LightingLook.Lerp(night, day, Step(m, 7 * 60));
            if (m < 17 * 60 - Blend) return day;
            if (m < 17 * 60 + Blend) return LightingLook.Lerp(day, sunset, Step(m, 17 * 60));
            if (m < 20 * 60 - Blend) return sunset;
            return LightingLook.Lerp(sunset, night, Step(m, 20 * 60));
        }

        /// <summary>Half the blend window, in minutes.</summary>
        public const float Blend = 15f;

        static float Step(float minute, float boundary) => Mathf.SmoothStep(0f, 1f, (minute - (boundary - Blend)) / (2f * Blend));
    }
}
