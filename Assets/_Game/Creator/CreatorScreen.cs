using Game.Characters.Building;
using Game.Characters.Creator;
using Game.Characters.Dna;
using Game.Characters.Generation;
using Game.Characters.Modular;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Game.Creator
{
    /// <summary>
    /// Runs the character creator: a <see cref="CreatorSession"/> edited through <see cref="CreatorView"/>, rebuilt
    /// into a live 3D preview by <see cref="CharacterBuilder"/> after every change. The camera eases between a
    /// full-body and a face view; dragging the preview turns the character.
    /// </summary>
    public sealed class CreatorScreen : MonoBehaviour
    {
        public CharacterContent Content;
        public UIDocument Document;
        public StyleSheet Style;
        public Camera PreviewCamera;
        public Transform Stage;
        public ulong StartSeed = 2026;

        // The UI panel covers the left of the screen. The camera looks down -Z, so screen-left is world +X: aiming at +X
        // shifts the character right, into the open part of the screen.
        static readonly (Vector3 Position, Vector3 Target) BodyView = (new Vector3(0.62f, 0.98f, 4.4f), new Vector3(0.62f, 0.88f, 0f));
        static readonly (Vector3 Position, Vector3 Target) FaceView = (new Vector3(0.2f, 1.66f, 1.25f), new Vector3(0.2f, 1.62f, 0f));

        CreatorSession _session;
        CharacterBuilder _builder;
        ModularCharacter _character;
        float _yaw;
        bool _faceView;

        public CreatorSession Session => _session;
        public ModularCharacter Character => _character;

        void Start()
        {
            var generator = new CharacterGenerator(Content.LoadCatalog());
            // Pick up where the player left off, if they've made a character before.
            CharacterDna start = PlayerLook.TryLoad(out CharacterDna saved) ? saved : generator.Generate(StartSeed);
            _session = new CreatorSession(generator, start, start.Seed);
            _builder = new CharacterBuilder(Content);
            _character = _builder.Build(_session.Dna, _session.Context, Stage);
            Poses.Relaxed(_character.transform);
            _session.Changed += Rebuild;

            VisualElement root = Document.rootVisualElement;
            if (Style != null) root.styleSheets.Add(Style);
            new CreatorView(root, _session, focus => _faceView = focus == "face", degrees => _yaw += degrees, Play);
            SnapCamera();
        }

        /// <summary>Scene the "Into town" button loads; it must be in the build's scene list.</summary>
        public string GameScene = "Town";

        void Play()
        {
            PlayerLook.Save(_session.Dna);
            SceneManager.LoadScene(GameScene);
        }

        void Rebuild()
        {
            _builder.Apply(_character, _session.Dna, _session.Context);
            Poses.Relaxed(_character.transform);
        }

        void Update()
        {
            if (_character == null) return;
            _character.transform.localRotation = Quaternion.Euler(0f, _yaw, 0f);
            (Vector3 position, Vector3 target) = _faceView ? FaceView : BodyView;
            float blend = 1f - Mathf.Exp(-8f * Time.unscaledDeltaTime);
            Transform cameraTransform = PreviewCamera.transform;
            cameraTransform.position = Vector3.Lerp(cameraTransform.position, Stage.TransformPoint(position), blend);
            Quaternion look = Quaternion.LookRotation(Stage.TransformPoint(target) - cameraTransform.position);
            cameraTransform.rotation = Quaternion.Slerp(cameraTransform.rotation, look, blend);
        }

        public void SnapCamera()
        {
            (Vector3 position, Vector3 target) = _faceView ? FaceView : BodyView;
            PreviewCamera.transform.position = Stage.TransformPoint(position);
            PreviewCamera.transform.LookAt(Stage.TransformPoint(target));
        }

        public void SetFaceView(bool face)
        {
            _faceView = face;
            SnapCamera();
        }
    }
}
