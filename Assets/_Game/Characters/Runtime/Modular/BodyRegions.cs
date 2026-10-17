using System;

namespace Game.Characters.Modular
{
    /// <summary>
    /// Body surface regions, used to hide skin under clothing. A region's ID is its flag's bit index; body meshes
    /// carry it in vertex colour red (ID / 255) and the body shader discards regions set in its _HiddenRegions mask.
    /// </summary>
    [Flags]
    public enum BodyRegions
    {
        None = 0,
        Head = 1 << 0,
        Neck = 1 << 1,
        UpperTorso = 1 << 2,
        LowerTorso = 1 << 3,
        Pelvis = 1 << 4,
        UpperArmL = 1 << 5,
        UpperArmR = 1 << 6,
        ForearmL = 1 << 7,
        ForearmR = 1 << 8,
        HandL = 1 << 9,
        HandR = 1 << 10,
        ThighL = 1 << 11,
        ThighR = 1 << 12,
        CalfL = 1 << 13,
        CalfR = 1 << 14,
        FootL = 1 << 15,
        FootR = 1 << 16,
    }

    public static class BodyRegionIds
    {
        /// <summary>The ID painted into body vertex colours for a single region.</summary>
        public static int IdOf(BodyRegions region)
        {
            int bits = (int)region;
            if (bits == 0 || (bits & (bits - 1)) != 0)
                throw new ArgumentException($"Expected exactly one region, got '{region}'.", nameof(region));

            int id = 0;
            while ((bits >>= 1) != 0) id++;
            return id;
        }
    }
}
