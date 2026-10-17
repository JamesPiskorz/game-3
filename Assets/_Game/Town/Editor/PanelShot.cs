using Game.Characters.Review;
using UnityEngine;

namespace Game.Town
{
    /// <summary>Composites a UI panel's render texture over a camera's and saves the PNG (headless screenshots).</summary>
    public static class PanelShot
    {
        public static RenderTexture Target(int width, int height, int antiAliasing = 1) =>
            new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = antiAliasing, hideFlags = HideFlags.DontSave };

        public static void Save(RenderTexture scene, RenderTexture ui, string path)
        {
            Texture2D background = Read(scene);
            Texture2D overlay = Read(ui);
            Color[] pixels = background.GetPixels(), top = overlay.GetPixels();
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = Color.Lerp(pixels[i], new Color(top[i].r, top[i].g, top[i].b), top[i].a);
            background.SetPixels(pixels);
            background.Apply();
            Snapshot.Save(background, path);
            Object.DestroyImmediate(background);
            Object.DestroyImmediate(overlay);
        }

        static Texture2D Read(RenderTexture source)
        {
            RenderTexture resolved = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(source, resolved);
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = resolved;
            var image = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
            image.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
            image.Apply();
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(resolved);
            return image;
        }
    }
}
