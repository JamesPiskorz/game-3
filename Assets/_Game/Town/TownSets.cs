using System.Collections.Generic;
using UnityEngine;

namespace Game.Town
{
    /// <summary>
    /// Blockout sets for each location, built from primitives: enough to tell the café from the bar at a glance
    /// until real environment art exists. Positions are in screen terms (x right, z away from camera), mirrored into
    /// world space by <see cref="TownStage.Screen"/>.
    /// </summary>
    public static class TownSets
    {
        public sealed class Set
        {
            public Color Floor;
            public Color Wall;
            public bool Outdoor;

            /// <summary>Warm lamps over the scene (interiors, and outdoors at night).</summary>
            public bool Lamps;
            public readonly List<Prop> Props = new List<Prop>();

            public Set Add(PrimitiveType shape, float x, float y, float z, float sx, float sy, float sz, Color colour, float yaw = 0f)
            {
                Props.Add(new Prop { Shape = shape, Position = new Vector3(x, y, z), Scale = new Vector3(sx, sy, sz), Colour = colour, Yaw = yaw });
                return this;
            }
        }

        public struct Prop
        {
            public PrimitiveType Shape;
            public Vector3 Position;
            public Vector3 Scale;
            public Color Colour;
            public float Yaw;
        }

        static Color C(int r, int g, int b) => new Color(r / 255f, g / 255f, b / 255f);

        public static Set For(string location)
        {
            switch (location)
            {
                case "cafe": return Cafe();
                case "park": return Park();
                case "campus": return Campus();
                case "office": return Office();
                case "gym": return Gym();
                case "mall": return Mall();
                case "bar": return Bar();
                case "library": return Library();
                default: return Home();
            }
        }

        static Set Table(Set set, float x, float z, Color top, Color leg) => set
            .Add(PrimitiveType.Cylinder, x, 0.74f, z, 0.8f, 0.02f, 0.8f, top)
            .Add(PrimitiveType.Cylinder, x, 0.37f, z, 0.08f, 0.37f, 0.08f, leg);

        static Set Cafe()
        {
            var set = new Set { Floor = C(150, 108, 78), Wall = C(232, 210, 180), Lamps = true };
            set.Add(PrimitiveType.Cube, 3.4f, 0.55f, -2.2f, 3.2f, 1.1f, 0.7f, C(110, 72, 52))          // counter
               .Add(PrimitiveType.Cube, 3.4f, 1.12f, -2.2f, 3.2f, 0.06f, 0.75f, C(240, 236, 228))
               .Add(PrimitiveType.Cube, 3.4f, 2.3f, -2.85f, 3.4f, 0.9f, 0.1f, C(70, 52, 44))           // menu board
               .Add(PrimitiveType.Cylinder, 2.6f, 1.25f, -2.2f, 0.18f, 0.1f, 0.18f, C(200, 80, 70))   // coffee machine-ish
               .Add(PrimitiveType.Cube, -3.6f, 1.6f, -2.9f, 1.6f, 1.2f, 0.05f, C(170, 210, 230));      // window
            Table(set, -3.1f, 0.6f, C(240, 236, 228), C(60, 50, 46));
            Table(set, 4.0f, 0.4f, C(240, 236, 228), C(60, 50, 46));
            return set;
        }

        static Set Park()
        {
            var set = new Set { Floor = C(104, 150, 86), Wall = C(150, 196, 230), Outdoor = true };
            void Tree(float x, float z, float size)
            {
                set.Add(PrimitiveType.Cylinder, x, 0.9f * size, z, 0.25f * size, 0.9f * size, 0.25f * size, C(110, 80, 60))
                   .Add(PrimitiveType.Sphere, x, 2.3f * size, z, 1.8f * size, 1.6f * size, 1.8f * size, C(80, 140, 82));
            }
            Tree(-4.2f, -2.6f, 1.2f);
            Tree(4.6f, -3.2f, 1.4f);
            Tree(-1.2f, -5.5f, 1.5f);
            Tree(2.0f, -6.5f, 1.3f);
            set.Add(PrimitiveType.Cube, 3.6f, 0.45f, -1.2f, 1.8f, 0.08f, 0.5f, C(150, 100, 70), -15f)    // bench
               .Add(PrimitiveType.Cube, 3.6f, 0.22f, -1.2f, 1.6f, 0.44f, 0.08f, C(70, 70, 76), -15f)
               .Add(PrimitiveType.Cylinder, 0f, 0.005f, 0.2f, 3.4f, 0.005f, 9f, C(196, 180, 150));      // path
            return set;
        }

        static Set Campus()
        {
            var set = new Set { Floor = C(170, 168, 160), Wall = C(160, 190, 222), Outdoor = true };
            set.Add(PrimitiveType.Cube, 0f, 2.5f, -5f, 12f, 5f, 1f, C(184, 120, 96))                      // brick building
               .Add(PrimitiveType.Cube, 0f, 1.2f, -4.45f, 1.6f, 2.4f, 0.1f, C(60, 64, 80));               // door
            for (int i = -2; i <= 2; i++)
                if (i != 0) set.Add(PrimitiveType.Cube, i * 2.2f, 3f, -4.45f, 1.1f, 1.3f, 0.1f, C(190, 220, 240));
            set.Add(PrimitiveType.Cylinder, -3.8f, 0.5f, -1.5f, 1.6f, 0.5f, 1.6f, C(150, 150, 150))       // planter
               .Add(PrimitiveType.Sphere, -3.8f, 1.2f, -1.5f, 1.4f, 0.9f, 1.4f, C(90, 140, 80));
            return set;
        }

        static Set Office()
        {
            var set = new Set { Floor = C(120, 124, 132), Wall = C(214, 220, 228), Lamps = true };
            for (int i = 0; i < 3; i++)
                set.Add(PrimitiveType.Cube, -3.5f + i * 3.5f, 0.75f, -2.6f, 1.6f, 0.05f, 0.8f, C(236, 236, 236))
                   .Add(PrimitiveType.Cube, -3.5f + i * 3.5f, 1.05f, -2.85f, 0.7f, 0.45f, 0.04f, C(40, 44, 54));
            set.Add(PrimitiveType.Cube, 0f, 1.8f, -3.4f, 10f, 2.2f, 0.05f, C(170, 200, 220));             // glass wall
            return set;
        }

        static Set Gym()
        {
            var set = new Set { Floor = C(64, 66, 74), Wall = C(196, 204, 212), Lamps = true };
            set.Add(PrimitiveType.Cube, -3.6f, 0.9f, -2.6f, 0.15f, 1.8f, 1.6f, C(50, 50, 56))             // rack
               .Add(PrimitiveType.Cube, 3.8f, 0.4f, -2.2f, 2f, 0.8f, 0.8f, C(60, 60, 66))                  // treadmill
               .Add(PrimitiveType.Cube, 3.8f, 1.1f, -2.55f, 0.9f, 0.6f, 0.1f, C(40, 40, 46))
               .Add(PrimitiveType.Cube, 0f, 2.2f, -3.2f, 4f, 0.5f, 0.05f, C(230, 90, 70));                  // banner
            for (int i = 0; i < 4; i++)
                set.Add(PrimitiveType.Cylinder, -3.45f, 0.4f + i * 0.4f, -2.6f, 0.35f, 0.04f, 0.35f, C(40, 40, 44));
            return set;
        }

        static Set Mall()
        {
            var set = new Set { Floor = C(220, 214, 206), Wall = C(240, 236, 232), Lamps = true };
            Color[] awnings = { C(230, 110, 140), C(90, 170, 200), C(240, 190, 80) };
            for (int i = 0; i < 3; i++)
            {
                float x = -4f + i * 4f;
                set.Add(PrimitiveType.Cube, x, 1.4f, -3f, 3.4f, 2.8f, 0.3f, C(250, 248, 244))
                   .Add(PrimitiveType.Cube, x, 1.2f, -2.82f, 2.6f, 1.8f, 0.05f, C(190, 220, 236))
                   .Add(PrimitiveType.Cube, x, 2.55f, -2.6f, 3.4f, 0.12f, 0.8f, awnings[i]);
            }
            set.Add(PrimitiveType.Cylinder, 5.6f, 0.4f, -1.6f, 1.2f, 0.4f, 1.2f, C(200, 196, 190))         // planter
               .Add(PrimitiveType.Sphere, 5.6f, 1.1f, -1.6f, 1f, 0.9f, 1f, C(100, 160, 96));
            return set;
        }

        static Set Bar()
        {
            var set = new Set { Floor = C(70, 48, 40), Wall = C(60, 42, 50), Lamps = true };
            set.Add(PrimitiveType.Cube, 0f, 0.55f, -2.4f, 7f, 1.1f, 0.7f, C(96, 56, 40))                 // bar counter
               .Add(PrimitiveType.Cube, 0f, 1.12f, -2.4f, 7.1f, 0.06f, 0.8f, C(40, 30, 30))
               .Add(PrimitiveType.Cube, 0f, 1.8f, -3.2f, 6f, 1.2f, 0.25f, C(80, 50, 46));               // shelf
            for (int i = 0; i < 9; i++)
                set.Add(PrimitiveType.Cylinder, -2.6f + i * 0.65f, 1.65f + (i % 2) * 0.05f, -3.05f, 0.1f, 0.14f, 0.1f,
                    Color.HSVToRGB(i * 0.13f % 1f, 0.6f, 0.8f));
            for (int i = 0; i < 5; i++)
                set.Add(PrimitiveType.Cylinder, -2.8f + i * 1.4f, 0.38f, -1.8f, 0.4f, 0.38f, 0.4f, C(150, 40, 50));
            return set;
        }

        static Set Library()
        {
            var set = new Set { Floor = C(130, 92, 64), Wall = C(226, 214, 190), Lamps = true };
            for (int i = 0; i < 4; i++)
            {
                float x = -4.5f + i * 3f;
                set.Add(PrimitiveType.Cube, x, 1.2f, -2.9f, 2.2f, 2.4f, 0.5f, C(100, 66, 44));
                for (int shelf = 0; shelf < 4; shelf++)
                    set.Add(PrimitiveType.Cube, x, 0.35f + shelf * 0.55f, -2.62f, 2f, 0.4f, 0.05f, Color.HSVToRGB((i * 0.27f + shelf * 0.11f) % 1f, 0.45f, 0.62f));
            }
            Table(set, 3.8f, 0.8f, C(120, 84, 58), C(80, 56, 40));
            return set;
        }

        static Set Home()
        {
            var set = new Set { Floor = C(170, 140, 116), Wall = C(222, 206, 214), Lamps = true };
            set.Add(PrimitiveType.Cube, 0f, 0.35f, -2.4f, 2.6f, 0.7f, 0.9f, C(120, 140, 170))               // sofa
               .Add(PrimitiveType.Cube, 0f, 0.8f, -2.8f, 2.6f, 0.6f, 0.2f, C(110, 130, 160))
               .Add(PrimitiveType.Cylinder, 0f, 0.005f, -1f, 3f, 0.005f, 2.2f, C(200, 120, 120))             // rug
               .Add(PrimitiveType.Cylinder, 2.2f, 0.8f, -2.5f, 0.05f, 0.8f, 0.05f, C(60, 60, 60))             // lamp
               .Add(PrimitiveType.Sphere, 2.2f, 1.65f, -2.5f, 0.45f, 0.35f, 0.45f, C(250, 230, 190));
            return set;
        }
    }
}
