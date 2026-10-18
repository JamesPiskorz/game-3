using System;
using Game.Characters.Rendering;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace Game.Town
{
    /// <summary>
    /// Imports environment prop FBX files from Content/Props (made by Tools/Blender/clean_generated_prop.py, graphics
    /// plan G5.1) ready to use, with no hand set-up:
    /// - import settings: no rig, animation, cameras, lights or colliders; authored normals; no imported tangents;
    /// - each material named pal_RRGGBB becomes a Game/Environment/Toon material in that colour, tuned like the blockout
    ///   props (TownProp.mat): the shader takes one flat colour per material, so the Blender side splits by colour;
    /// - smoothed outline normals baked into the tangents, as for the characters and the blockout props.
    /// </summary>
    public sealed class PropModelImporter : AssetPostprocessor
    {
        public const string Folder = "Assets/_Game/Town/Content/Props/";
        public const string MaterialPrefix = "pal_";
        const string Template = "Assets/_Game/Town/Materials/TownProp.mat";
        const string Shader = "Game/Environment/Toon";

        // Bump to reimport every prop when this importer's behaviour changes.
        public override uint GetVersion() => 1;

        // After URP's material description preprocessors, which would otherwise leave URP Lit on the materials.
        public override int GetPostprocessOrder() => 100;

        bool Applies => assetPath.StartsWith(Folder, StringComparison.Ordinal) && assetPath.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase);

        void OnPreprocessModel()
        {
            if (!Applies) return;
            var importer = (ModelImporter)assetImporter;
            importer.animationType = ModelImporterAnimationType.None;
            importer.importAnimation = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importBlendShapes = false;
            importer.addCollider = false;
            importer.importNormals = ModelImporterNormals.Import;      // sharp edges are split in Blender
            importer.importTangents = ModelImporterTangents.None;      // tangents carry outline normals instead
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            importer.materialLocation = ModelImporterMaterialLocation.InPrefab;
        }

        void OnPreprocessMaterialDescription(MaterialDescription description, Material material, AnimationClip[] animations)
        {
            if (!Applies) return;
            context.DependsOnSourceAsset(Template);
            var template = AssetDatabase.LoadAssetAtPath<Material>(Template);
            if (template != null)
            {
                material.shader = template.shader;
                material.CopyPropertiesFromMaterial(template);
            }
            else material.shader = UnityEngine.Shader.Find(Shader);

            if (TryParseColour(description.materialName, out Color colour)) material.SetColor("_BaseColor", colour);
            else context.LogImportWarning($"Material '{description.materialName}' isn't named {MaterialPrefix}RRGGBB, so it keeps the template's grey.");
        }

        void OnPostprocessModel(GameObject root)
        {
            if (!Applies) return;
            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
                OutlineNormals.Bake(filter.sharedMesh);
        }

        /// <summary>The sRGB colour in a material name such as pal_966C4E.</summary>
        public static bool TryParseColour(string materialName, out Color colour)
        {
            colour = default;
            if (materialName == null || !materialName.StartsWith(MaterialPrefix, StringComparison.Ordinal)) return false;
            string hex = materialName.Substring(MaterialPrefix.Length);
            return hex.Length == 6 && ColorUtility.TryParseHtmlString("#" + hex, out colour);
        }
    }
}
