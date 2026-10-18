using UnityEngine;

namespace Game.Characters.Rendering
{
    /// <summary>A named lighting preset (day, sunset, night, interior, studio): data art can tune without code.</summary>
    [CreateAssetMenu(menuName = "Game/Rendering/Lighting Preset")]
    public sealed class LightingPreset : ScriptableObject
    {
        public LightingLook Look;
    }
}
