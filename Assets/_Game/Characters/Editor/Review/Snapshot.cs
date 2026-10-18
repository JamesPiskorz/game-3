using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Game.Characters.Review
{
    /// <summary>Renders cameras to images and stacks images into contact sheets, headless-safe.</summary>
    public static class Snapshot
    {
        static bool _warmedUp;

        public static Camera CreateCamera(Color background, float fieldOfView = 30f)
        {
            var camera = new GameObject("Snapshot Camera").AddComponent<Camera>();
            Game.Characters.Rendering.GameCamera.Configure(camera); // post and MSAA as in the game
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = background;
            camera.fieldOfView = fieldOfView;
            camera.nearClipPlane = 0.05f;
            return camera;
        }

        /// <summary>
        /// Renders <paramref name="camera"/> from <paramref name="position"/> looking at <paramref name="target"/>, with
        /// the game's MSAA, so review images show what the player sees.
        /// </summary>
        public static Texture2D Shoot(Camera camera, Vector3 position, Vector3 target, int width, int height)
        {
            camera.transform.position = position;
            camera.transform.LookAt(target);
            return Shoot(camera, width, height);
        }

        /// <summary>Renders <paramref name="camera"/> as it is posed, with the game's MSAA.</summary>
        public static Texture2D Shoot(Camera camera, int width, int height)
        {
            // Async compilation renders placeholder shaders until variants finish compiling; snapshots must wait.
            UnityEditor.ShaderUtil.allowAsyncCompilation = false;
            RenderTexture multisampled = Game.Characters.Rendering.GameCamera.CaptureTarget(width, height);
            RenderTexture resolved = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            camera.targetTexture = multisampled;
            if (!_warmedUp)
            {
                camera.Render(); // the first render after the pipeline starts comes out wrong; throw it away
                _warmedUp = true;
            }
            camera.Render();
            camera.targetTexture = null;
            Graphics.Blit(multisampled, resolved);

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = resolved;
            // Survives NewScene's unload of unused assets, so sheets can span several scenes.
            var image = new Texture2D(width, height, TextureFormat.RGB24, false) { hideFlags = HideFlags.HideAndDontSave };
            image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            image.Apply();
            RenderTexture.active = previous;

            RenderTexture.ReleaseTemporary(resolved);
            Object.DestroyImmediate(multisampled);
            return image;
        }

        /// <summary>Stacks images top to bottom (first image on top), left-aligned on <paramref name="background"/>.</summary>
        public static Texture2D StackVertically(IReadOnlyList<Texture2D> images, Color background)
        {
            int width = 0, height = 0;
            foreach (Texture2D image in images)
            {
                width = Mathf.Max(width, image.width);
                height += image.height;
            }

            var sheet = new Texture2D(width, height, TextureFormat.RGB24, false) { hideFlags = HideFlags.HideAndDontSave };
            var fill = new Color32[width * height];
            Color32 background32 = background;
            for (int i = 0; i < fill.Length; i++) fill[i] = background32;
            sheet.SetPixels32(fill);

            int y = height;
            foreach (Texture2D image in images)
            {
                y -= image.height; // texture rows start at the bottom
                sheet.SetPixels(0, y, image.width, image.height, image.GetPixels());
            }
            sheet.Apply();
            return sheet;
        }

        /// <summary>Lays images out in rows of <paramref name="columns"/> (reading order).</summary>
        public static Texture2D Grid(IReadOnlyList<Texture2D> images, int columns, Color background)
        {
            int rows = (images.Count + columns - 1) / columns;
            int cellWidth = images[0].width, cellHeight = images[0].height;
            var sheet = new Texture2D(cellWidth * columns, cellHeight * rows, TextureFormat.RGB24, false) { hideFlags = HideFlags.HideAndDontSave };
            var fill = new Color32[sheet.width * sheet.height];
            Color32 background32 = background;
            for (int i = 0; i < fill.Length; i++) fill[i] = background32;
            sheet.SetPixels32(fill);

            for (int i = 0; i < images.Count; i++)
            {
                int column = i % columns, row = i / columns;
                sheet.SetPixels(column * cellWidth, (rows - 1 - row) * cellHeight, cellWidth, cellHeight, images[i].GetPixels());
            }
            sheet.Apply();
            return sheet;
        }

        public static void Save(Texture2D image, string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, image.EncodeToPNG());
        }
    }
}
