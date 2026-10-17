using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.Characters.Review
{
    /// <summary>
    /// The lighting setups every character review runs under (PLAN.md §3.4): skin and colours must hold up in all
    /// of them, not just in daylight.
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

        public static readonly LightingRig Day = new LightingRig
        {
            Name = "day", Background = new Color(0.62f, 0.74f, 0.86f), Ambient = new Color(0.42f, 0.46f, 0.55f),
            SunColour = new Color(1f, 0.96f, 0.9f), SunIntensity = 1.1f, SunAngles = new Vector3(42f, 155f, 0f),
        };

        public static readonly LightingRig Sunset = new LightingRig
        {
            Name = "sunset", Background = new Color(0.55f, 0.36f, 0.42f), Ambient = new Color(0.3f, 0.24f, 0.36f),
            SunColour = new Color(1f, 0.62f, 0.38f), SunIntensity = 1.05f, SunAngles = new Vector3(14f, 118f, 0f),
        };

        public static readonly LightingRig Night = new LightingRig
        {
            Name = "night", Background = new Color(0.07f, 0.08f, 0.13f), Ambient = new Color(0.1f, 0.11f, 0.18f),
            SunColour = new Color(0.45f, 0.55f, 0.8f), SunIntensity = 0.35f, SunAngles = new Vector3(48f, 150f, 0f),
            LampColour = new Color(1f, 0.74f, 0.45f),
        };

        public static readonly LightingRig[] All = { Day, Sunset, Night };

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
            RenderSettings.sun = sun;
            created.Add(sun.gameObject);

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = Ambient;
            var probe = new SphericalHarmonicsL2();
            probe.AddAmbientLight(Ambient);
            RenderSettings.ambientProbe = probe;

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
