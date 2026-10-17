using System;
using Game.Characters.Dna;
using UnityEngine;

namespace Game.Characters.Building
{
    /// <summary>
    /// The player's own character, kept between scenes and sessions as a DNA share code in PlayerPrefs: the
    /// creator saves it, the town dresses the player in it.
    /// </summary>
    public static class PlayerLook
    {
        public const string Key = "Game.PlayerDna";
        public const string NameKey = "Game.PlayerName";
        public const int MaxNameLength = 16;

        /// <summary>The name residents call the player by; null until one is chosen.</summary>
        public static string Name
        {
            get
            {
                string name = PlayerPrefs.GetString(NameKey, "").Trim();
                return name.Length == 0 ? null : name;
            }
            set
            {
                string name = (value ?? "").Trim();
                if (name.Length > MaxNameLength) name = name.Substring(0, MaxNameLength);
                if (name.Length == 0) PlayerPrefs.DeleteKey(NameKey);
                else PlayerPrefs.SetString(NameKey, name);
                PlayerPrefs.Save();
            }
        }

        public static void Save(CharacterDna dna)
        {
            PlayerPrefs.SetString(Key, DnaSerializer.ToShareCode(dna));
            PlayerPrefs.Save();
        }

        public static bool TryLoad(out CharacterDna dna)
        {
            dna = null;
            string code = PlayerPrefs.GetString(Key, "");
            if (code.Length == 0) return false;
            try
            {
                dna = DnaSerializer.FromShareCode(code);
                return true;
            }
            catch (FormatException e)
            {
                Debug.LogWarning("[PlayerLook] The saved look is unreadable: " + e.Message);
                return false;
            }
        }
    }
}
