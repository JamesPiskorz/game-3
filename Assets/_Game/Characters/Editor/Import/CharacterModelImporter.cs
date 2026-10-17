using System;
using System.IO;
using System.Linq;
using Game.Characters.Modular;
using Game.Characters.Rendering;
using UnityEditor;
using UnityEngine;

namespace Game.Characters.Import
{
    /// <summary>
    /// Imports character FBX files from Content/Imported (exported by Tools/Blender/character_tools.py) ready to use,
    /// with no hand set-up (PLAN.md §4):
    /// - import settings: generic rig, blendshapes kept with authored normals, no imported materials or tangents;
    /// - smoothed outline normals baked into tangents (decision 0002);
    /// - toon material templates assigned;
    /// - the sidecar &lt;name&gt;.character.json turned into a ModularCharacter (body) or CharacterPart (slot, covered
    ///   regions, blocked slots, spring chains).
    /// </summary>
    public sealed class CharacterModelImporter : AssetPostprocessor
    {
        public const string Folder = "Assets/_Game/Characters/Content/Imported/";
        const string SkinTemplate = "Assets/_Game/Characters/Content/StandIn/Materials/StandIn_Skin.mat";
        const string ClothTemplate = "Assets/_Game/Characters/Content/StandIn/Materials/StandIn_Cloth.mat";

        // Bump to reimport every character model when this importer's behaviour changes.
        public override uint GetVersion() => 2;

        bool Applies => assetPath.StartsWith(Folder, StringComparison.Ordinal) && assetPath.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase);

        [Serializable]
        sealed class Sidecar
        {
            public string kind = "part";
            public string slot;
            public string[] covers = Array.Empty<string>();
            public string[] blocks = Array.Empty<string>();
            public string[] springChains = Array.Empty<string>();
            public bool followsBodyShapes = true;
            public bool small;
        }

        void OnPreprocessModel()
        {
            if (!Applies) return;
            var importer = (ModelImporter)assetImporter;
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.importAnimation = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importBlendShapes = true;
            importer.importNormals = ModelImporterNormals.Import;
            importer.importBlendShapeNormals = ModelImporterNormals.None; // body shapes mustn't disturb toon normals
            importer.importTangents = ModelImporterTangents.None;          // tangents carry outline normals instead
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            // Readable: runtime mesh work (identity bake, merging, BakeMesh checks) needs the CPU copy.
            importer.isReadable = true;
        }

        void OnPostprocessModel(GameObject root)
        {
            if (!Applies) return;

            string sidecarPath = Path.ChangeExtension(assetPath, ".character.json");
            context.DependsOnSourceAsset(sidecarPath);
            if (!File.Exists(sidecarPath))
            {
                context.LogImportWarning($"No {Path.GetFileName(sidecarPath)} beside the model; it was imported as a plain mesh.");
                return;
            }
            Sidecar sidecar = JsonUtility.FromJson<Sidecar>(File.ReadAllText(sidecarPath));

            bool body = sidecar.kind == "body";
            var template = AssetDatabase.LoadAssetAtPath<Material>(body ? SkinTemplate : ClothTemplate);
            SkinnedMeshRenderer[] renderers = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            foreach (SkinnedMeshRenderer renderer in renderers)
            {
                OutlineNormals.Bake(renderer.sharedMesh);
                if (template != null)
                    renderer.sharedMaterials = Enumerable.Repeat(template, renderer.sharedMesh.subMeshCount).ToArray();
            }

            Transform armature = root.transform.Find("Armature");
            if (armature == null)
            {
                context.LogImportError("Character models need their skeleton under a child named 'Armature'.");
                return;
            }

            if (body)
            {
                if (renderers.Length != 1) context.LogImportError($"A body needs exactly one skinned mesh; found {renderers.Length}.");
                else root.AddComponent<ModularCharacter>().Initialize(armature, renderers[0]);
                return;
            }

            var part = root.AddComponent<CharacterPart>();
            part.Slot = sidecar.slot;
            part.BlocksSlots = sidecar.blocks ?? Array.Empty<string>();
            part.SpringChains = sidecar.springChains ?? Array.Empty<string>();
            part.FollowsBodyShapes = sidecar.followsBodyShapes;
            part.Small = sidecar.small;
            foreach (string region in sidecar.covers ?? Array.Empty<string>())
            {
                if (Enum.TryParse(region, out BodyRegions parsed)) part.CoveredRegions |= parsed;
                else context.LogImportError($"Unknown body region '{region}' in {Path.GetFileName(sidecarPath)}.");
            }
        }
    }
}
