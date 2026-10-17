using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Characters.Modular
{
    /// <summary>
    /// Assembles a character from a body and wearable parts at runtime: rebinds each part onto the body's skeleton,
    /// mirrors body shapes onto it, and hides the skin it covers. Spike B prototype of the Builder (PLAN.md §3.6).
    /// </summary>
    [ExecuteAlways] // so OnDestroy releases the material instance in edit mode too
    public sealed class ModularCharacter : MonoBehaviour
    {
        static readonly int HiddenRegionsId = Shader.PropertyToID("_HiddenRegions");

        [SerializeField] Transform _skeletonRoot;
        [SerializeField] SkinnedMeshRenderer _body;

        SkeletonBinder _binder;
        Material _bodyMaterial;
        readonly Dictionary<string, Equipped> _equipped = new Dictionary<string, Equipped>(StringComparer.Ordinal);
        readonly Dictionary<string, float> _shapes = new Dictionary<string, float>(StringComparer.Ordinal);

        sealed class Equipped
        {
            public CharacterPart Part;
            public SkinnedMeshRenderer[] Renderers;
            public List<Transform> GraftedBones;
            public List<Material> Materials;
        }

        public SkinnedMeshRenderer Body => _body;
        public BodyRegions HiddenRegions { get; private set; }

        public void Initialize(Transform skeletonRoot, SkinnedMeshRenderer body)
        {
            _skeletonRoot = skeletonRoot;
            _body = body;
            _binder = null;
        }

        public CharacterPart GetPart(string slot) => _equipped.TryGetValue(slot, out Equipped equipped) ? equipped.Part : null;

        /// <summary>The body's own material instance; recolour it freely.</summary>
        public Material BodyMaterial
        {
            get
            {
                EnsureReady();
                return _bodyMaterial;
            }
        }

        /// <summary>Material instances owned by the part in <paramref name="slot"/>; recolour them freely.</summary>
        public IReadOnlyList<Material> PartMaterials(string slot) =>
            _equipped.TryGetValue(slot, out Equipped equipped) ? equipped.Materials : (IReadOnlyList<Material>)Array.Empty<Material>();

        public IEnumerable<string> EquippedSlots => _equipped.Keys;

        /// <summary>
        /// Instantiates <paramref name="prefab"/> on this character, replacing the part in its slot and any part
        /// either side blocks (a dress blocks top and bottom; a top displaces a dress).
        /// </summary>
        public CharacterPart Equip(CharacterPart prefab)
        {
            EnsureReady();
            var displaced = new List<string>();
            foreach (KeyValuePair<string, Equipped> pair in _equipped)
                if (pair.Key == prefab.Slot
                    || Array.IndexOf(prefab.BlocksSlots, pair.Key) >= 0
                    || Array.IndexOf(pair.Value.Part.BlocksSlots, prefab.Slot) >= 0)
                    displaced.Add(pair.Key);
            foreach (string slot in displaced)
                Unequip(slot);

            CharacterPart part = Instantiate(prefab, transform);
            part.name = prefab.name;
            SkinnedMeshRenderer[] renderers = part.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var grafted = new List<Transform>();
            var materials = new List<Material>();
            foreach (SkinnedMeshRenderer renderer in renderers)
            {
                grafted.AddRange(_binder.Rebind(renderer));
                if (part.FollowsBodyShapes) WarnAboutMissingShapes(part, renderer);
                foreach (KeyValuePair<string, float> shape in _shapes)
                    SetWeight(renderer, shape.Key, shape.Value);

                Material[] shared = renderer.sharedMaterials;
                for (int i = 0; i < shared.Length; i++)
                {
                    if (shared[i] == null) continue;
                    shared[i] = Instance(shared[i]);
                    materials.Add(shared[i]);
                }
                renderer.sharedMaterials = shared;
            }

            // The renderers now use our skeleton, so the part's own copy is dead weight.
            Transform ownSkeleton = part.transform.Find(_skeletonRoot.name);
            if (ownSkeleton != null) DestroySafely(ownSkeleton.gameObject);

            _equipped[prefab.Slot] = new Equipped { Part = part, Renderers = renderers, GraftedBones = grafted, Materials = materials };
            UpdateHiddenRegions();
            return part;
        }

        public void Unequip(string slot)
        {
            if (!_equipped.TryGetValue(slot, out Equipped equipped)) return;

            _equipped.Remove(slot);
            foreach (Transform bone in equipped.GraftedBones)
            {
                _binder.Forget(bone);
                DestroySafely(bone.gameObject);
            }
            if (equipped.Part != null) DestroySafely(equipped.Part.gameObject);
            foreach (Material material in equipped.Materials)
                DestroySafely(material);
            UpdateHiddenRegions();
        }

        /// <summary>Sets a body shape (0..1) on the body and on every part with a blendshape of the same name.</summary>
        public void SetShape(string shape, float weight)
        {
            EnsureReady();
            _shapes[shape] = weight;
            SetWeight(_body, shape, weight);
            foreach (Equipped equipped in _equipped.Values)
                foreach (SkinnedMeshRenderer renderer in equipped.Renderers)
                    SetWeight(renderer, shape, weight);
        }

        /// <summary>Blendshapes <paramref name="body"/> has and <paramref name="part"/> lacks: each one clips when used.</summary>
        public static List<string> MissingBodyShapes(Mesh body, Mesh part)
        {
            var missing = new List<string>();
            for (int i = 0; i < body.blendShapeCount; i++)
            {
                string shape = body.GetBlendShapeName(i);
                if (part.GetBlendShapeIndex(shape) < 0) missing.Add(shape);
            }
            return missing;
        }

        void EnsureReady()
        {
            if (_binder != null) return;
            if (_skeletonRoot == null || _body == null)
                throw new InvalidOperationException($"{nameof(ModularCharacter)} on '{name}' needs a skeleton root and a body renderer.");

            _binder = new SkeletonBinder(_skeletonRoot);
            _bodyMaterial = Instance(_body.sharedMaterial);
            _body.sharedMaterial = _bodyMaterial;
        }

        // Per-character material instances rather than MaterialPropertyBlocks, which would opt renderers out of the SRP Batcher.
        static Material Instance(Material template) =>
            new Material(template) { name = template.name + " (Instance)", hideFlags = HideFlags.DontSave };

        void UpdateHiddenRegions()
        {
            BodyRegions hidden = BodyRegions.None;
            foreach (Equipped equipped in _equipped.Values)
                hidden |= equipped.Part.CoveredRegions;

            HiddenRegions = hidden;
            _bodyMaterial.SetInteger(HiddenRegionsId, (int)hidden);
        }

        void WarnAboutMissingShapes(CharacterPart part, SkinnedMeshRenderer renderer)
        {
            List<string> missing = MissingBodyShapes(_body.sharedMesh, renderer.sharedMesh);
            if (missing.Count > 0)
                Debug.LogWarning($"Part '{part.name}' ({renderer.name}) lacks body shapes {string.Join(", ", missing)}; it will clip when they are used.", part);
        }

        // Looks shapes up by name on every call; fine for a spike, cached or baked away in the real Builder.
        static void SetWeight(SkinnedMeshRenderer renderer, string shape, float weight)
        {
            int index = renderer.sharedMesh.GetBlendShapeIndex(shape);
            if (index >= 0) renderer.SetBlendShapeWeight(index, weight * 100f);
        }

        void OnDestroy()
        {
            if (_bodyMaterial != null) DestroySafely(_bodyMaterial);
            foreach (Equipped equipped in _equipped.Values)
                foreach (Material material in equipped.Materials)
                    if (material != null) DestroySafely(material);
        }

        static void DestroySafely(UnityEngine.Object obj)
        {
            if (Application.isPlaying) Destroy(obj);
            else DestroyImmediate(obj);
        }
    }
}
