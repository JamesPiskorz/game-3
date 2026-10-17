using System.Collections.Generic;
using Game.Characters.Modular;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.Spikes.SpikeB
{
    /// <summary>
    /// Play-mode demo: equips the outfit on Start, drives body shapes from the inspector sliders, and toggles
    /// outfit parts with the number keys (1 = first part, 2 = second...).
    /// </summary>
    public sealed class SpikeBDemo : MonoBehaviour
    {
        public ModularCharacter Character;
        public List<CharacterPart> Outfit = new List<CharacterPart>();

        [Range(0f, 1f)] public float Weight;
        [Range(0f, 1f)] public float Chest;
        [Range(0f, 1f)] public float Hips;

        void Start()
        {
            foreach (CharacterPart part in Outfit)
                Character.Equip(part);
        }

        void Update()
        {
            Character.SetShape("body.weight", Weight);
            Character.SetShape("body.chest", Chest);
            Character.SetShape("body.hips", Hips);

            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;
            for (int i = 0; i < Outfit.Count && i < 9; i++)
            {
                if (!keyboard[Key.Digit1 + i].wasPressedThisFrame) continue;
                CharacterPart part = Outfit[i];
                if (Character.GetPart(part.Slot) != null) Character.Unequip(part.Slot);
                else Character.Equip(part);
            }
        }
    }
}
