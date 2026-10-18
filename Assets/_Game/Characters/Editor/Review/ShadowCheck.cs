using Game.Characters.Building;
using Game.Characters.ContentAuthoring;
using Game.Characters.Generation;
using Game.Characters.Modular;
using Game.Characters.Rendering;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.Characters.Review
{
    /// <summary>
    /// Checks graphics-plan R2-R3: a character never shadows itself, and is in the environment's shadow as a whole or
    /// not at all. A shadow-only board is held between the sun and the character: once low (over the legs and body),
    /// once high (over the head), and once big enough to cover everything. The low board must leave the character as it
    /// is in the open, and the high board must put all of it in shadow, exactly as the big one does. A character half
    /// in shadow fails both. Headless: -executeMethod Game.Characters.Review.ShadowCheck.Run (exits 0 pass, 1 fail).
    /// </summary>
    public static class ShadowCheck
    {
        const int Width = 360, Height = 640;
        const float Different = 0.1f;   // a pixel differs if its brightness is more than 10% off
        const float AtMost = 0.03f;     // share of the character's pixels allowed to differ

        [MenuItem("Game/Characters/Review/Run Shadow Check")]
        public static void Run()
        {
            (float low, float high) = Measure();
            bool pass = low <= AtMost && high <= AtMost;
            Debug.Log($"[ShadowCheck] {(pass ? "PASS" : "FAIL")}: with a board over the body, {low:P1} of the character differs from it " +
                      $"in the open; with a board over the head, {high:P1} differs from it fully in shadow (each at most {AtMost:P0}).");
            if (Application.isBatchMode) EditorApplication.Exit(pass ? 0 : 1);
        }

        /// <summary>
        /// Share of the character's pixels that differ from the expected render: in the open for a board over the body,
        /// fully shadowed for a board over the head.
        /// </summary>
        public static (float Low, float High) Measure()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            CharacterContent content = StandInContent.Load();
            ModularCharacter character = new CharacterBuilder(content).Build(new CharacterGenerator(content.LoadCatalog()).Generate(11));
            Poses.Relaxed(character.transform);
            LightingRig.Day.Apply(new[] { Vector3.zero });
            Vector3 towardSun = -RenderSettings.sun.transform.forward;
            character.GetComponent<CharacterShadowAnchor>().Apply(); // LateUpdate doesn't run in edit mode

            Camera camera = Snapshot.CreateCamera(new Color(1f, 0f, 1f));
            Vector3 target = new Vector3(0f, 0.93f, 0f), eye = target + new Vector3(0f, 0.05f, 3.7f);
            Texture2D Shoot(Vector3? shadowOn, Vector3 size)
            {
                GameObject board = null;
                if (shadowOn.HasValue)
                {
                    // Facing the sun, 1.5 m toward it from where its shadow falls; it casts a shadow but isn't seen.
                    board = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    board.transform.SetPositionAndRotation(shadowOn.Value + towardSun * 1.5f, Quaternion.LookRotation(towardSun));
                    board.transform.localScale = size;
                    board.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.ShadowsOnly;
                }
                Texture2D image = Snapshot.Shoot(camera, eye, target, Width, Height);
                if (board != null) Object.DestroyImmediate(board);
                return image;
            }

            Texture2D open = Shoot(null, Vector3.zero);
            Texture2D shadowed = Shoot(new Vector3(0f, 1f, 0f), new Vector3(4f, 4f, 0.05f));
            Texture2D low = Shoot(new Vector3(-0.12f, 0.75f, 0f), new Vector3(0.7f, 0.55f, 0.05f));
            Texture2D high = Shoot(new Vector3(0f, 1.62f, 0f), new Vector3(0.7f, 0.55f, 0.05f));
            return (Differs(open, low), Differs(shadowed, high));
        }

        // Share of the character's pixels (anything but the magenta background) whose brightness is more than 10% off.
        static float Differs(Texture2D expected, Texture2D actual)
        {
            Color[] a = expected.GetPixels(), b = actual.GetPixels();
            int character = 0, differing = 0;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i].r > 0.95f && a[i].g < 0.05f && a[i].b > 0.95f) continue;
                character++;
                float x = Luma(a[i]), y = Luma(b[i]);
                if (Mathf.Abs(x - y) > Different * Mathf.Max(x, 0.05f)) differing++;
            }
            return character == 0 ? 0f : differing / (float)character;
        }

        static float Luma(Color c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
    }
}
