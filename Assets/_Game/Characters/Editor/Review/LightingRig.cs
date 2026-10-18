using System.Collections.Generic;
using Game.Characters.Rendering;
using UnityEngine;

namespace Game.Characters.Review
{
    /// <summary>
    /// The lighting setups every character review runs under (PLAN.md §3.4): skin and colours must hold up in all
    /// of them, not just in daylight. They come from the game's lighting presets (<see cref="LightingPresets"/>), so a
    /// review shows the light the game uses.
    /// </summary>
    public sealed class LightingRig
    {
        public string Name;
        public Color Background;
        public Color Ambient;
        public Color SunColour;
        public float SunIntensity;
        public Vector3 SunAngles;
        public Color? LampColour;    // warm point lights near the subjects (street lamps, interiors)
        public LightingLook Look;

        public static LightingRig Day => From(LightingLibrary.Day);
        public static LightingRig Sunset => From(LightingLibrary.Sunset);
        public static LightingRig Night => From(LightingLibrary.Night);
        public static LightingRig[] All => new[] { Day, Sunset, Night };

        public static LightingRig From(string preset)
        {
            LightingLook look = LightingPresets.Library().Get(preset).Look;
            return new LightingRig
            {
                Name = preset, Look = look, Background = look.SkyHorizon, Ambient = look.AmbientEquator, SunColour = look.SunColour,
                SunIntensity = look.SunIntensity, SunAngles = look.SunAngles, LampColour = look.Lamps ? look.LampColour : (Color?)null,
            };
        }

        /// <summary>Lights the open scene; lamps are placed in front of and above each subject position.</summary>
        public List<GameObject> Apply(IEnumerable<Vector3> subjects)
        {
            var created = new List<GameObject>();

            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = SunColour;
            sun.intensity = SunIntensity;
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(SunAngles);
            sun.shadowStrength = Look.ShadowStrength > 0f ? Look.ShadowStrength : 1f;
            RenderSettings.sun = sun;
            created.Add(sun.gameObject);
            SceneLighting.ApplyAmbient(Look);

            if (LampColour.HasValue)
                foreach (Vector3 subject in subjects)
                {
                    var lamp = new GameObject("Lamp").AddComponent<Light>();
                    lamp.type = LightType.Point;
                    lamp.color = LampColour.Value;
                    lamp.intensity = 0.8f;
                    lamp.range = 3.2f;
                    lamp.transform.position = subject + new Vector3(0.9f, 2.1f, 1.1f);
                    created.Add(lamp.gameObject);
                }

            return created;
        }
    }
}
