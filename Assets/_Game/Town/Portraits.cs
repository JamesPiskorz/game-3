using System;
using System.Collections.Generic;
using System.Linq;
using Game.Characters.Building;
using Game.Characters.Dna;
using Game.Characters.Expressions;
using Game.Characters.Modular;
using Game.Characters.Rendering;
using Game.Characters.Springs;
using Game.Sim;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.Town
{
    /// <summary>
    /// Head-and-shoulders portraits for the HUD (contacts, the resident card), so the player can tell generated people
    /// apart at a glance. Each is rendered from the resident's DNA in their everyday clothes, in a small studio far below
    /// the town under the same daylight whatever the hour, and cached until their look changes (a makeover or a gift
    /// replaces their DNA object).
    /// </summary>
    public sealed class Portraits : IDisposable
    {
        public const int Size = 160;
        static readonly Vector3 StudioPosition = new Vector3(0f, -500f, 0f);
        static readonly Color Backdrop = new Color(0.86f, 0.8f, 0.88f);

        readonly CharacterBuilder _builder;
        readonly Light _sun;
        readonly GameObject _studio;
        readonly Camera _camera;
        readonly Dictionary<int, (CharacterDna Dna, int Version, RenderTexture Texture)> _cache = new Dictionary<int, (CharacterDna, int, RenderTexture)>();

        /// <summary>The cache key for the player's own portrait.</summary>
        const int PlayerKey = -1;
        ModularCharacter _sitter;

        public Portraits(CharacterBuilder builder, Light sun)
        {
            _builder = builder;
            _sun = sun;
            _studio = new GameObject("Portrait studio") { hideFlags = HideFlags.DontSave };
            _studio.transform.position = StudioPosition;
            _camera = new GameObject("Portrait camera").AddComponent<Camera>();
            _camera.transform.SetParent(_studio.transform, false);
            _camera.enabled = false; // rendered by hand
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = Backdrop;
            _camera.fieldOfView = 18f;
            _camera.nearClipPlane = 0.1f;
            _camera.farClipPlane = 4f;
        }

        /// <summary>The resident's portrait, rendering it if it's new or their look has changed.</summary>
        public Texture For(Resident resident) => For(resident.Index, resident.Dna, 0);

        /// <summary>The player's portrait; <paramref name="version"/> is their look version, which edits in place bump.</summary>
        public Texture ForPlayer(CharacterDna dna, int version) => dna == null ? null : For(PlayerKey, dna, version);

        Texture For(int key, CharacterDna dna, int version)
        {
            if (_cache.TryGetValue(key, out var cached) && cached.Dna == dna && cached.Version == version) return cached.Texture;
            if (cached.Texture != null) cached.Texture.Release();
            RenderTexture texture = cached.Texture ?? new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4, hideFlags = HideFlags.DontSave };
            Render(dna, texture);
            _cache[key] = (dna, version, texture);
            return texture;
        }

        void Render(CharacterDna dna, RenderTexture target)
        {
            if (_sitter == null)
            {
                _sitter = _builder.Build(dna, Sim.Sandbox.TownSession.DayOutfit, _studio.transform);
                _sitter.name = "Sitter";
                if (_sitter.GetComponent<IdleMotion>() is IdleMotion idle) idle.enabled = false; // a still pose
            }
            else
            {
                _builder.Apply(_sitter, dna, Sim.Sandbox.TownSession.DayOutfit);
            }
            foreach (SpringBoneChain chain in _sitter.GetComponents<SpringBoneChain>()) chain.enabled = false;
            _sitter.GetComponent<CharacterLod>()?.Apply(0f);
            Poses.Relaxed(_sitter.transform);
            if (_sitter.GetComponent<FaceExpression>() is FaceExpression face)
            {
                face.Show("happy", 0.45f, float.PositiveInfinity);
                face.Snap();
            }

            // Frame the head and shoulders from a little below eye level.
            Transform head = _sitter.GetComponentsInChildren<Transform>().First(t => t.name == "Head");
            Vector3 look = head.position + new Vector3(0f, 0.1f, 0f);
            _camera.transform.position = look + new Vector3(0f, -0.02f, 1.12f);
            _camera.transform.LookAt(look - new Vector3(0f, 0.02f, 0f));

            // The same soft daylight for every portrait, whatever it's like in town right now.
            Color sunColour = _sun.color, ambient = RenderSettings.ambientLight;
            float intensity = _sun.intensity, shadows = _sun.shadowStrength;
            Quaternion rotation = _sun.transform.rotation;
            SphericalHarmonicsL2 probe = RenderSettings.ambientProbe;
            try
            {
                _sun.color = new Color(1f, 0.96f, 0.9f);
                _sun.intensity = 1.05f;
                _sun.shadowStrength = 0.6f;
                _sun.transform.rotation = Quaternion.Euler(35f, 160f, 0f);
                var light = new Color(0.46f, 0.47f, 0.55f);
                RenderSettings.ambientLight = light;
                var flat = new SphericalHarmonicsL2();
                flat.AddAmbientLight(light);
                RenderSettings.ambientProbe = flat;
                _camera.targetTexture = target;
                _camera.Render();
            }
            finally
            {
                _camera.targetTexture = null;
                _sun.color = sunColour;
                _sun.intensity = intensity;
                _sun.shadowStrength = shadows;
                _sun.transform.rotation = rotation;
                RenderSettings.ambientLight = ambient;
                RenderSettings.ambientProbe = probe;
            }
        }

        public void Dispose()
        {
            foreach ((CharacterDna _, int _, RenderTexture texture) in _cache.Values)
                if (texture != null) { texture.Release(); UnityEngine.Object.Destroy(texture); }
            _cache.Clear();
            if (_studio != null) UnityEngine.Object.Destroy(_studio);
        }
    }
}
