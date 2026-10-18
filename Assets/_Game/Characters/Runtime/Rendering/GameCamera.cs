using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Game.Characters.Rendering
{
    /// <summary>
    /// How every camera renders: post-processing on (the pipeline asset's volume profile), MSAA from the pipeline
    /// asset, HDR. The town, creator, menu, portrait studio and review renders all go through here, so review images
    /// show what the player sees (graphics plan G0.1-G0.3).
    /// </summary>
    public static class GameCamera
    {
        /// <param name="sceneGrading">
        /// False for cameras that shouldn't get the scene's screen grading (the portrait studio, review tiles): they see
        /// only the pipeline's profile, not volumes on the Default layer (<see cref="SceneLighting"/>).
        /// </param>
        public static void Configure(Camera camera, bool sceneGrading = true)
        {
            camera.allowMSAA = true;
            camera.allowHDR = true;
            UniversalAdditionalCameraData data = camera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            // MSAA covers the outlines, which are geometry. No TAA: it ghosts thin hair and lines, and the toon shader
            // has no motion-vector pass.
            data.antialiasing = AntialiasingMode.None;
            data.volumeLayerMask = sceneGrading ? 1 : 0;
        }

        /// <summary>
        /// The MSAA sample count the pipeline renders the screen with. A camera rendering into a texture takes its
        /// sample count from the texture instead, so captures must make theirs with this many samples to match.
        /// </summary>
        public static int MsaaSamples => GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset asset ? asset.msaaSampleCount : 1;

        /// <summary>A capture target that renders exactly as the screen does.</summary>
        public static RenderTexture CaptureTarget(int width, int height) =>
            new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = MsaaSamples };
    }
}
