using System.Collections.Generic;
using System.Linq;
using Game.Characters.Building;
using Game.Characters.Dna;
using Game.Characters.Expressions;
using Game.Characters.Modular;
using Game.Characters.Rendering;
using Game.Sim;
using Game.Sim.Relationships;
using Game.Sim.Sandbox;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.Town
{
    /// <summary>
    /// The 3D view of wherever the player is: a blockout set, the time of day's light, the player and up to
    /// <see cref="MaxShown"/> residents built from their DNA in the right outfits. Characters are kept between
    /// redraws and only rebuilt when who's there, or what they're wearing, changes.
    /// </summary>
    public sealed class TownStage : MonoBehaviour
    {
        public const int MaxShown = 6;

        public CharacterContent Content;
        public Material PropMaterial;
        public Material HighlightMaterial;
        public Camera Camera;
        public Light Sun;
        public Material RainMaterial;

        CharacterBuilder _builder;
        readonly Dictionary<int, Shown> _residents = new Dictionary<int, Shown>();

        void Awake()
        {
            if (Camera != null) Game.Characters.Rendering.GameCamera.Configure(Camera);
        }
        readonly Dictionary<Color, Material> _materials = new Dictionary<Color, Material>();
        readonly List<Light> _lamps = new List<Light>();
        Shown _player;
        Transform _set;
        string _setId;
        GameObject _ring;
        Vector3 _cameraPosition, _cameraTarget;
        InteractionResult _reacted;

        sealed class Shown
        {
            public ModularCharacter Character;
            public string Outfit;
            public CharacterDna Dna;
            public int Version;

            /// <summary>Where they're heading and how they'll stand once there.</summary>
            public Vector3 Target;
            public Quaternion Facing;

            /// <summary>Walking off; removed on arrival.</summary>
            public bool Leaving;

            /// <summary>Sits once they reach their spot (a seat).</summary>
            public bool Seated;

            public Transform Head;
        }

        List<Vector3> _seats = new List<Vector3>();

        /// <summary>Walking speed in metres per second.</summary>
        public float WalkSpeed = 1.3f;

        // Characters walking off, until they're out of view.
        readonly List<Shown> _leaving = new List<Shown>();

        /// <summary>Positions are authored in screen terms (x right, z away from camera); the camera looks down -Z, so screen-right is world -X.</summary>
        public static Vector3 Screen(float x, float y, float z) => new Vector3(-x, y, z);

        public void Show(TownSession session)
        {
            _builder ??= new CharacterBuilder(Content);
            SimWorld world = session.World;
            // A new place appears as it is; at the same place, people come and go on foot.
            bool arrived = _setId != session.Location;
            if (arrived) BuildSet(session.Location);
            Light(TownSets.For(session.Location), world.Now.Hour * 60 + world.Now.Minute, world.Weather);

            bool date = session.DateWith != null;
            _player = Dress(_player, world.Player.Dna, world.Player.Outfit ?? TownSession.DayOutfit, "Player", world.Player.LookVersion);

            List<Resident> present = Grouped(session.Present(), world);
            List<Resident> shown = present.Take(MaxShown).ToList();
            if (session.Selected != null && !shown.Contains(session.Selected))
                shown[shown.Count - 1] = session.Selected;

            foreach (int gone in _residents.Keys.Where(index => shown.All(r => r.Index != index)).ToList())
            {
                Shown leaver = _residents[gone];
                _residents.Remove(gone);
                if (arrived) ModularCharacter.DestroySafely(leaver.Character.gameObject);
                else
                {
                    leaver.Leaving = true;
                    leaver.Target = Offstage(leaver.Character.transform.localPosition);
                    _leaving.Add(leaver);
                }
            }
            if (arrived)
            {
                foreach (Shown leaver in _leaving) ModularCharacter.DestroySafely(leaver.Character.gameObject);
                _leaving.Clear();
            }
            var newcomers = new List<Shown>();
            foreach (Resident resident in shown)
            {
                _residents.TryGetValue(resident.Index, out Shown existing);
                string outfit = date ? TownSession.DateOutfit : world.OutfitFor(resident, session.Location);
                _residents[resident.Index] = Dress(existing, resident.Dna, outfit, resident.Name);
                if (existing == null) newcomers.Add(_residents[resident.Index]);
            }

            Arrange(shown, session.Selected, date, world);
            // Newcomers walk in from the side nearest their spot, unless the player has just got here.
            foreach (Shown newcomer in newcomers)
            {
                Transform body = newcomer.Character.transform;
                body.localPosition = arrived ? newcomer.Target : Offstage(newcomer.Target);
                body.localRotation = newcomer.Facing;
            }
            if (arrived) SnapCharacters();
            React(session);
        }

        /// <summary>World position just above resident <paramref name="index"/>'s head, if they're on stage.</summary>
        public Vector3? HeadPosition(int index)
        {
            if (!_residents.TryGetValue(index, out Shown shown) || shown.Character == null) return null;
            if (shown.Head == null) shown.Head = shown.Character.GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name == "Head");
            return shown.Head == null ? (Vector3?)null : shown.Head.position + Vector3.up * 0.3f;
        }

        /// <summary>A point out of view on the side of the stage nearest <paramref name="from"/>.</summary>
        static Vector3 Offstage(Vector3 from) => new Vector3(from.x >= 0f ? 9f : -9f, 0f, from.z - 1f);

        /// <summary>Puts everyone where they're going, facing the right way, at once (arrivals and screenshots).</summary>
        public void SnapCharacters()
        {
            foreach (Shown shown in _residents.Values.Append(_player).Where(s => s != null))
            {
                shown.Character.transform.localPosition = shown.Target;
                shown.Character.transform.localRotation = shown.Facing;
                var idle = shown.Character.GetComponent<IdleMotion>();
                if (idle == null) continue;
                idle.Walking = 0f;
                idle.SnapWalking();
                idle.Sitting = shown.Seated ? 1f : 0f;
                idle.SnapSitting();
            }
            foreach (Shown leaver in _leaving) ModularCharacter.DestroySafely(leaver.Character.gameObject);
            _leaving.Clear();
        }

        /// <summary>Walks everyone towards their spot: facing where they're going, then turning to face the scene.</summary>
        void Walk(Shown shown, float dt)
        {
            Transform body = shown.Character.transform;
            Vector3 to = shown.Target - body.localPosition;
            to.y = 0f;
            bool moving = to.magnitude > 0.03f;
            var idle = shown.Character.GetComponent<IdleMotion>();
            if (idle != null)
            {
                idle.Walking = moving ? 1f : 0f;
                idle.Sitting = !moving && shown.Seated ? 1f : 0f;
            }
            float turn = 1f - Mathf.Exp(-8f * dt);
            if (moving)
            {
                body.localPosition = Vector3.MoveTowards(body.localPosition, shown.Target, WalkSpeed * dt);
                body.localRotation = Quaternion.Slerp(body.localRotation, Quaternion.LookRotation(to.normalized, Vector3.up), turn);
            }
            else
            {
                body.localPosition = shown.Target;
                body.localRotation = Quaternion.Slerp(body.localRotation, shown.Facing, turn);
            }
        }

        /// <summary>Couples and friends stand together: each resident is followed by whoever they're with.</summary>
        static List<Resident> Grouped(List<Resident> present, SimWorld world)
        {
            var ordered = new List<Resident>();
            foreach (Resident resident in present)
            {
                if (ordered.Contains(resident)) continue;
                ordered.Add(resident);
                foreach (Resident other in present)
                    if (!ordered.Contains(other) && (world.Social.AreCouple(resident.Index, other.Index) || world.Social.AreFriends(resident.Index, other.Index)))
                    {
                        ordered.Add(other);
                        break;
                    }
            }
            return ordered;
        }

        /// <summary>The speaker's face answers the latest interaction (the date's outcome, if it was a date).</summary>
        void React(TownSession session)
        {
            if (session.Last == null || session.Last == _reacted || session.Speaker == null) return;
            _reacted = session.Last;
            if (!_residents.TryGetValue(session.Speaker.Index, out Shown speaker)) return;
            string reaction = session.Last.FollowUpReaction ?? session.Last.Reaction;
            (string expression, float strength) = ReactionFaces.For(reaction, session.Speaker.Profile.Tone);
            speaker.Character.GetComponent<FaceExpression>()?.Show(expression, strength);
            speaker.Character.GetComponent<IdleMotion>()?.Nod();
        }

        Shown Dress(Shown shown, CharacterDna dna, string outfit, string name, int version = 0)
        {
            if (shown != null && shown.Dna == dna && shown.Outfit == outfit && shown.Version == version) return shown;
            if (shown == null)
            {
                ModularCharacter character = _builder.Build(dna, outfit, transform);
                character.name = name;
                shown = new Shown { Character = character };
            }
            else
            {
                _builder.Apply(shown.Character, dna, outfit);
            }
            shown.Dna = dna;
            shown.Outfit = outfit;
            shown.Version = version;
            Poses.Relaxed(shown.Character.transform);
            return shown;
        }

        void Arrange(List<Resident> shown, Resident selected, bool date, SimWorld world)
        {
            Vector3 camera = Screen(0.75f, 1.65f, 7.2f);
            bool Together(Resident a, Resident b) => world.Social.AreCouple(a.Index, b.Index) || world.Social.AreFriends(a.Index, b.Index);

            // Where the place has seats, a few people sit: those on their own (not standing with a friend), not being
            // talked to and not at work here, taken from the end of the line. Whoever the player turns to stands up.
            var seats = new Dictionary<int, Vector3>();
            if (!date)
            {
                var free = new Queue<Vector3>(_seats.Select(seat => Screen(seat.x, 0f, seat.z)));
                for (int i = shown.Count - 1; i >= 0 && free.Count > 0; i--)
                {
                    Resident r = shown[i];
                    bool withSomeone = (i > 0 && Together(r, shown[i - 1])) || (i + 1 < shown.Count && Together(r, shown[i + 1]));
                    if (r == selected || withSomeone || world.OutfitFor(r, _setId) == TownSession.WorkOutfit) continue;
                    seats[r.Index] = free.Dequeue();
                }
            }
            List<Resident> standing = shown.Where(r => !seats.ContainsKey(r.Index)).ToList();
            var positions = new Dictionary<int, Vector3>(seats);
            for (int i = 0; i < standing.Count; i++)
            {
                float offset = i - (standing.Count - 1) / 2f;
                positions[standing[i].Index] = date ? Screen(0.55f, 0f, 0.3f) : Screen(0.6f + offset * 1.15f, 0f, -0.12f * offset * offset);
            }

            // The player stands at the group's left, or steps up beside whoever they're talking to.
            Vector3 player;
            if (date) player = Screen(-0.55f, 0f, 0.55f);
            else if (selected != null && positions.TryGetValue(selected.Index, out Vector3 partner)) player = partner + Screen(-0.95f, 0f, 0.8f);
            else player = Screen(standing.Count == 0 ? -0.6f : standing.Min(r => -positions[r.Index].x) - 1.1f, 0f, 0.9f);

            for (int i = 0; i < shown.Count; i++)
            {
                Resident resident = shown[i];
                Shown spot = _residents[resident.Index];
                Vector3 at = positions[resident.Index];
                spot.Target = at;
                spot.Seated = seats.ContainsKey(resident.Index);
                if (spot.Seated)
                {
                    spot.Facing = Flat(camera - at); // seated facing the room, over the table
                    continue;
                }
                // Everyone half-turns to the camera; the selected resident turns to the player, and people standing
                // with a friend or partner turn towards them as if mid-conversation.
                Resident companion = null;
                foreach (int j in new[] { i - 1, i + 1 })
                    if (j >= 0 && j < shown.Count && shown[j] != selected
                        && (world.Social.AreCouple(resident.Index, shown[j].Index) || world.Social.AreFriends(resident.Index, shown[j].Index)))
                        companion = shown[j];
                Vector3 facing = resident == selected ? (player - at) + (camera - at) * 0.4f
                    : companion != null ? (positions[companion.Index] - at) * 2f + (camera - at) * 0.35f
                    : (player - at) * 0.4f + (camera - at);
                spot.Facing = Flat(facing);
            }

            _player.Target = player;
            Vector3 focus = selected != null && positions.TryGetValue(selected.Index, out Vector3 s) ? s : Screen(0.6f, 0f, 0f);
            _player.Facing = Flat((focus - player) + (camera - player) * 0.35f);

            if (_ring == null) _ring = Prop(PrimitiveType.Cylinder, transform, HighlightMaterial);
            _ring.SetActive(selected != null && positions.ContainsKey(selected.Index));
            if (_ring.activeSelf)
            {
                _ring.transform.localPosition = positions[selected.Index] + Vector3.up * 0.012f;
                _ring.transform.localScale = new Vector3(0.55f, 0.004f, 0.55f);
            }

            // Frame within the open part of the screen: the HUD panel covers the right quarter, so the subject sits
            // about 13% of the view's width left of centre.
            if (selected != null && positions.TryGetValue(selected.Index, out Vector3 other))
            {
                float distance = 4.2f + Vector3.Distance(player, other) * 0.7f;
                _cameraTarget = (player + other) * 0.5f + Vector3.up * 1.08f + Screen(OpenAreaShift * distance, 0f, 0f);
                _cameraPosition = _cameraTarget + Screen(0f, 0.4f, distance);
            }
            else
            {
                List<Vector3> everyone = positions.Values.Append(player).ToList();
                float left = everyone.Min(p => -p.x), right = everyone.Max(p => -p.x);
                // The player stands nearer the camera, so leave extra room for perspective at the edges.
                float distance = Mathf.Max(9f, (right - left + 1f) * 1.5f);
                _cameraTarget = Screen((left + right) * 0.5f + OpenAreaShift * distance, 0.95f, 0f);
                _cameraPosition = _cameraTarget + Screen(0f, 0.85f, distance);
            }
        }

        ParticleSystem _rain;

        /// <summary>Streaks of rain over the stage, started already falling.</summary>
        void Rain(bool on)
        {
            if (!on)
            {
                if (_rain != null) _rain.gameObject.SetActive(false);
                return;
            }
            if (_rain == null)
            {
                var go = new GameObject("Rain");
                go.transform.SetParent(transform, false);
                go.transform.localPosition = new Vector3(0f, 7f, -1f);
                _rain = go.AddComponent<ParticleSystem>();
                _rain.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                ParticleSystem.MainModule main = _rain.main;
                main.duration = 1f;
                main.loop = true;
                main.startLifetime = 1.1f;
                main.startSpeed = 0f;
                main.startSize = 0.012f;
                main.maxParticles = 4000;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                ParticleSystem.VelocityOverLifetimeModule velocity = _rain.velocityOverLifetime;
                velocity.enabled = true;
                velocity.space = ParticleSystemSimulationSpace.World;
                velocity.x = new ParticleSystem.MinMaxCurve(-0.6f);
                velocity.y = new ParticleSystem.MinMaxCurve(-9f);
                velocity.z = new ParticleSystem.MinMaxCurve(0f);
                ParticleSystem.EmissionModule emission = _rain.emission;
                emission.rateOverTime = 2200f;
                ParticleSystem.ShapeModule shape = _rain.shape;
                shape.shapeType = ParticleSystemShapeType.Box;
                shape.scale = new Vector3(20f, 0.1f, 12f);
                var renderer = go.GetComponent<ParticleSystemRenderer>();
                renderer.renderMode = ParticleSystemRenderMode.Stretch;
                renderer.velocityScale = 0.035f;
                renderer.lengthScale = 1f;
                renderer.sharedMaterial = RainMaterial;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            if (_rain.gameObject.activeSelf && _rain.isPlaying) return;
            _rain.gameObject.SetActive(true);
            _rain.Simulate(1.5f, true, true);
            _rain.Play();
        }

        // Fraction of the view's width (at the subject's distance, for this field of view) to shift the subject left.
        const float OpenAreaShift = 0.13f;

        static Quaternion Flat(Vector3 direction)
        {
            direction.y = 0f;
            return direction.sqrMagnitude < 1e-6f ? Quaternion.identity : Quaternion.LookRotation(direction.normalized, Vector3.up);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            foreach (Shown shown in _residents.Values) Walk(shown, dt);
            if (_player != null) Walk(_player, dt);
            for (int i = _leaving.Count - 1; i >= 0; i--)
            {
                Walk(_leaving[i], dt);
                if (Vector3.Distance(_leaving[i].Character.transform.localPosition, _leaving[i].Target) < 0.05f)
                {
                    ModularCharacter.DestroySafely(_leaving[i].Character.gameObject);
                    _leaving.RemoveAt(i);
                }
            }

            if (Camera == null) return;
            float blend = 1f - Mathf.Exp(-5f * Time.unscaledDeltaTime);
            Transform cam = Camera.transform;
            cam.position = Vector3.Lerp(cam.position, transform.TransformPoint(_cameraPosition), blend);
            Quaternion look = Quaternion.LookRotation(transform.TransformPoint(_cameraTarget) - cam.position);
            cam.rotation = Quaternion.Slerp(cam.rotation, look, blend);
        }

        public void SnapCamera(bool characters = true)
        {
            if (characters) SnapCharacters();
            Camera.transform.position = transform.TransformPoint(_cameraPosition);
            Camera.transform.LookAt(transform.TransformPoint(_cameraTarget));
        }

        void BuildSet(string location)
        {
            if (_set != null) ModularCharacter.DestroySafely(_set.gameObject);
            _set = new GameObject("Set " + location).transform;
            _set.SetParent(transform, false);
            _setId = location;

            TownSets.Set set = TownSets.For(location);
            _seats = set.Seats.ToList();
            _glowing.Clear();
            _floorColour = set.Floor;
            _floor = Place(PrimitiveType.Cube, new Vector3(0f, -0.05f, -2f), new Vector3(30f, 0.1f, 24f), set.Floor).GetComponent<Renderer>();
            if (!set.Outdoor)
            {
                Place(PrimitiveType.Cube, Screen(0f, 2.5f, -3.6f), new Vector3(30f, 5f, 0.2f), set.Wall);
                Place(PrimitiveType.Cube, Screen(-7.5f, 2.5f, 0f), new Vector3(0.2f, 5f, 8f), set.Wall * 0.92f);
            }
            foreach (TownSets.Prop prop in set.Props)
            {
                GameObject made = Place(prop.Shape, Screen(prop.Position.x, prop.Position.y, prop.Position.z), prop.Scale, prop.Colour, prop.Glow);
                made.transform.localRotation = Quaternion.Euler(0f, -prop.Yaw, 0f);
            }
        }

        GameObject Place(PrimitiveType shape, Vector3 position, Vector3 scale, Color colour, bool glow = false)
        {
            GameObject made = Prop(shape, _set, glow ? Glowing(colour) : Material(colour));
            made.transform.localPosition = position;
            made.transform.localScale = scale;
            return made;
        }

        static GameObject Prop(PrimitiveType shape, Transform parent, Material material)
        {
            GameObject made = GameObject.CreatePrimitive(shape);
            Collider collider = made.GetComponent<Collider>();
            if (collider != null) ModularCharacter.DestroySafely(collider);
            made.transform.SetParent(parent, false);
            made.GetComponent<Renderer>().sharedMaterial = material;
            made.GetComponent<MeshFilter>().sharedMesh = Outlined(shape, made.GetComponent<MeshFilter>().sharedMesh);
            return made;
        }

        static readonly Dictionary<PrimitiveType, Mesh> OutlinedMeshes = new Dictionary<PrimitiveType, Mesh>();

        // The primitive with outline normals baked into its tangents, so the toon outline stays closed at hard corners.
        static Mesh Outlined(PrimitiveType shape, Mesh primitive)
        {
            if (OutlinedMeshes.TryGetValue(shape, out Mesh mesh) && mesh != null) return mesh;
            mesh = Object.Instantiate(primitive);
            mesh.name = primitive.name + " (outlined)";
            mesh.hideFlags = HideFlags.DontSave;
            OutlineNormals.Bake(mesh);
            OutlinedMeshes[shape] = mesh;
            return mesh;
        }

        readonly List<Material> _glowing = new List<Material>();
        Renderer _floor;
        Color _floorColour;

        // Lit windows and lamps: their own material, so the glow can follow the light.
        Material Glowing(Color colour)
        {
            colour.a = 1f;
            var material = new Material(PropMaterial) { name = "Lit " + ColorUtility.ToHtmlStringRGB(colour), hideFlags = HideFlags.DontSave };
            material.SetColor("_BaseColor", colour);
            _glowing.Add(material);
            return material;
        }

        Material Material(Color colour)
        {
            colour.a = 1f;
            if (_materials.TryGetValue(colour, out Material material)) return material;
            material = new Material(PropMaterial) { name = "Prop " + ColorUtility.ToHtmlStringRGB(colour), hideFlags = HideFlags.DontSave };
            material.SetColor("_BaseColor", colour);
            _materials[colour] = material;
            return material;
        }

        SceneLighting _lighting;

        /// <summary>
        /// Outdoors follows the clock through the lighting presets (day, sunset, night with street lamps), blended over
        /// half an hour at each change; interiors have warm overhead light and the walls as background, whatever the
        /// hour. Clouds and rain grey outdoor light; it rains on outdoor sets.
        /// </summary>
        void Light(TownSets.Set set, int minuteOfDay, Weather weather)
        {
            LightingLibrary library = Content.Lighting;
            LightingLook look;
            if (!set.Outdoor)
            {
                look = library.Get(LightingLibrary.Interior).Look.Walls(set.Wall);
                look.Lamps = set.Lamps;
            }
            else
            {
                look = library.Outdoors(minuteOfDay);
                if (weather == Weather.Rainy) look = look.Overcast(0.65f, 0.35f);
                else if (weather == Weather.Cloudy) look = look.Overcast(0.4f, 0.6f);
            }
            Rain(set.Outdoor && weather == Weather.Rainy);
            _lighting ??= new SceneLighting(Sun, Camera);
            _lighting.Apply(look, set.Outdoor ? (Color?)null : set.Wall);
            bool lamps = look.Lamps;
            foreach (Material glowing in _glowing) glowing.SetColor("_EmissionColor", look.LampColour * look.Glow);
            // Rain darkens the ground.
            if (_floor != null) _floor.sharedMaterial = Material(set.Outdoor && weather == Weather.Rainy ? _floorColour * 0.78f : _floorColour);

            while (_lamps.Count < 2)
            {
                var lamp = new GameObject("Lamp").AddComponent<Light>();
                lamp.type = LightType.Point;
                lamp.transform.SetParent(transform, false);
                _lamps.Add(lamp);
            }
            for (int i = 0; i < _lamps.Count; i++)
            {
                _lamps[i].gameObject.SetActive(lamps);
                _lamps[i].color = look.LampColour;
                _lamps[i].intensity = look.LampIntensity;
                _lamps[i].range = 5f;
                // Outdoors at night, one lamp is the key light and casts shadows (characters don't receive them).
                _lamps[i].shadows = i == 0 && set.Outdoor && lamps ? LightShadows.Soft : LightShadows.None;
                _lamps[i].shadowStrength = 0.8f;
                _lamps[i].transform.localPosition = Screen(i == 0 ? -1.2f : 1.6f, 2.6f, 1.6f);
            }
        }
    }
}
