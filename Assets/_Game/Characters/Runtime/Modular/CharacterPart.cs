using UnityEngine;

namespace Game.Characters.Modular
{
    /// <summary>
    /// Marks a prefab as a wearable part (clothing, hair, accessory) and says how it combines with the body.
    /// The prefab carries its own copy of the shared skeleton, as an FBX import does, under a child named like the
    /// character's skeleton root; equipping rebinds its renderers onto the wearer's skeleton and discards the copy.
    /// </summary>
    public sealed class CharacterPart : MonoBehaviour
    {
        [Tooltip("Slot the part occupies, e.g. \"top\", \"bottom\", \"hair\". Equipping replaces whatever is in the slot.")]
        public string Slot;

        [Tooltip("Other slots this part takes over, e.g. a dress blocks \"top\" and \"bottom\". Equipping either side removes the other.")]
        public string[] BlocksSlots = System.Array.Empty<string>();

        [Tooltip("Body regions this part fully covers. Their skin is hidden while the part is worn.")]
        public BodyRegions CoveredRegions;

        [Tooltip("Whether the part must carry every body blendshape. Clear it for parts on areas body shapes never move (hair, hats).")]
        public bool FollowsBodyShapes = true;

        [Tooltip("First bones of chains that swing with spring physics (hair tails, skirt panels).")]
        public string[] SpringChains = System.Array.Empty<string>();
    }
}
