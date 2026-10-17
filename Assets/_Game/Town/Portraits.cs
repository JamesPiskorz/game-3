using System;
using System.Collections.Generic;
using Game.Characters.Building;
using Game.Characters.Dna;
using Game.Sim;
using UnityEngine;

namespace Game.Town
{
    /// <summary>
    /// Head-and-shoulders portraits for the HUD (contacts, messages, the resident card, the player), so the player can
    /// tell generated people apart at a glance. Each is rendered from the DNA in everyday clothes by a
    /// <see cref="PortraitStudio"/> under the same daylight whatever the hour, and cached until the look changes (a
    /// makeover or a gift replaces a resident's DNA object; the player's look version counts edits made in place).
    /// </summary>
    public sealed class Portraits : IDisposable
    {
        public const int Size = 160;
        const int PlayerKey = -1;
        static readonly Color Backdrop = new Color(0.86f, 0.8f, 0.88f);

        readonly PortraitStudio _studio;
        readonly Dictionary<int, (CharacterDna Dna, int Version, RenderTexture Texture)> _cache = new Dictionary<int, (CharacterDna, int, RenderTexture)>();

        public Portraits(CharacterBuilder builder, Light sun) => _studio = new PortraitStudio(builder, Backdrop, sun);

        public Texture For(Resident resident) => For(resident.Index, resident.Dna, 0);

        /// <summary>The player's portrait; <paramref name="version"/> is their look version.</summary>
        public Texture ForPlayer(CharacterDna dna, int version) => dna == null ? null : For(PlayerKey, dna, version);

        Texture For(int key, CharacterDna dna, int version)
        {
            if (_cache.TryGetValue(key, out var cached) && cached.Dna == dna && cached.Version == version) return cached.Texture;
            if (cached.Texture != null) cached.Texture.Release();
            RenderTexture texture = cached.Texture ?? PortraitStudio.Target(Size, Size);
            _studio.Render(dna, Sim.Sandbox.TownSession.DayOutfit, texture, PortraitStudio.Framing.Face);
            _cache[key] = (dna, version, texture);
            return texture;
        }

        public void Dispose()
        {
            foreach ((CharacterDna _, int _, RenderTexture texture) in _cache.Values)
                if (texture != null)
                {
                    texture.Release();
                    UnityEngine.Object.Destroy(texture);
                }
            _cache.Clear();
            _studio.Dispose();
        }
    }
}
