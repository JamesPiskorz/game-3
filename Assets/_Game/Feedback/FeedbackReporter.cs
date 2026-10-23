using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Game.Feedback
{
    /// <summary>
    /// Feedback from inside the game, in every scene from start-up. F8, or the small Feedback button in the corner,
    /// freezes the moment (a screenshot and the whole game state, <see cref="FeedbackContext"/>), pauses the game and
    /// opens a form for what's wrong or what the idea is. Sending saves the ticket under feedback-tickets/ and files it,
    /// with any earlier ones still waiting, as a GitHub issue (<see cref="GitHubIssues"/>) in the background.
    /// <c>-feedback-no-file</c> keeps tickets local; <c>-feedback-no-button</c> hides the corner button.
    /// </summary>
    public sealed class FeedbackReporter : MonoBehaviour
    {
        public const Key Hotkey = Key.F8;
        const int WindowId = 0xFEED;

        public static FeedbackReporter Instance { get; private set; }

        enum State { Closed, Capturing, Writing, Sent }

        State _state;
        FeedbackTicket _ticket;
        Texture2D _shot;
        bool _includeShot = true, _focusTitle, _showButton, _filing;
        string _title = "", _description = "", _status, _progress, _issueUrl, _folder;
        int _category;
        float _timeScale = 1f;
        readonly List<VisualElement> _paused = new List<VisualElement>();
        readonly Queue<float> _frames = new Queue<float>();
        readonly ConcurrentQueue<Action> _fromFiling = new ConcurrentQueue<Action>();

        static bool FilingEnabled => !FeedbackPaths.HasFlag("-feedback-no-file");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Create()
        {
            if (Instance != null) return;
            var reporter = new GameObject("Feedback");
            DontDestroyOnLoad(reporter);
            Instance = reporter.AddComponent<FeedbackReporter>();
        }

        void Awake()
        {
            FeedbackPaths.Init();
            _showButton = !Application.isBatchMode && !FeedbackPaths.HasFlag("-feedback-no-button");
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (_windowBackground != null) Destroy(_windowBackground);
        }

        void Update()
        {
            _frames.Enqueue(Time.unscaledDeltaTime);
            while (_frames.Count > 120) _frames.Dequeue();
            while (_fromFiling.TryDequeue(out Action action)) action();

            Keyboard keyboard = Keyboard.current;
            if (keyboard == null || !keyboard[Hotkey].wasPressedThisFrame) return;
            if (_state == State.Closed) Open();
            else if (_state != State.Capturing) Close();
        }

        /// <summary>Takes the screenshot and the game state now, then opens the form.</summary>
        public void Open()
        {
            if (_state == State.Closed) StartCoroutine(CaptureThenOpen());
        }

        IEnumerator CaptureThenOpen()
        {
            _state = State.Capturing; // the corner button stays out of the screenshot
            yield return Application.isBatchMode ? null : new WaitForEndOfFrame();
            _ticket = Capture();
            _title = _description = "";
            _status = _progress = _issueUrl = _folder = null;
            _includeShot = _ticket.Screenshot != null;
            _focusTitle = true;
            _state = State.Writing;
            Pause();
        }

        /// <summary>
        /// Captures and saves a ticket without the form, for automated runs (the player smoke test): hands back its folder.
        /// Not filed.
        /// </summary>
        public IEnumerator SaveTicket(string title, string description, Action<string> saved)
        {
            if (_state != State.Closed) yield break;
            _state = State.Capturing;
            yield return Application.isBatchMode ? null : new WaitForEndOfFrame();
            FeedbackTicket ticket = Capture();
            DropScreenshot();
            _state = State.Closed;
            ticket.Title = title;
            ticket.Description = description;
            ticket.Category = "Other";
            saved(ticket.Save(FeedbackPaths.TicketsRoot));
        }

        FeedbackTicket Capture()
        {
            byte[] png = null;
            if (!Application.isBatchMode)
            {
                try
                {
                    // The finished frame, UI and all, read back at the end of the frame (the project has no screen capture module).
                    _shot = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
                    _shot.ReadPixels(new Rect(0f, 0f, Screen.width, Screen.height), 0, 0, false);
                    _shot.Apply(false);
                    png = _shot.EncodeToPNG();
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[Feedback] No screenshot: " + e.Message);
                }
            }
            FeedbackTicket ticket;
            try
            {
                ticket = FeedbackContext.Capture(_frames.ToList(), Time.timeScale);
            }
            catch (Exception e)
            {
                Debug.LogError("[Feedback] Couldn't gather the game state: " + e);
                ticket = new FeedbackTicket();
                ticket.Context["captureError"] = e.ToString();
            }
            ticket.Screenshot = png;
            return ticket;
        }

        void Send()
        {
            _ticket.Title = _title.Trim();
            _ticket.Description = _description.Trim();
            _ticket.Category = FeedbackTicket.Categories[_category];
            if (!_includeShot) _ticket.Screenshot = null;
            try
            {
                _folder = _ticket.Save(FeedbackPaths.TicketsRoot);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                _status = "Couldn't save the ticket: " + e.Message;
                return;
            }
            Debug.Log("[Feedback] Saved " + _folder);
            _state = State.Sent;
            _status = $"Saved as ticket {Path.GetFileName(_folder)}.";
            if (FilingEnabled) FileInBackground(_folder);
            else _status += " Not filed on GitHub: the game was started with -feedback-no-file.";
        }

        void FileInBackground(string mine)
        {
            if (_filing) return;
            _filing = true;
            _progress = "Filing it on GitHub…";
            string root = FeedbackPaths.TicketsRoot, repoRoot = FeedbackPaths.RepoRoot;
            Task.Run(() =>
            {
                List<GitHubIssues.Outcome> outcomes;
                try
                {
                    outcomes = GitHubIssues.FilePending(root, repoRoot, message => _fromFiling.Enqueue(() => _progress = message));
                }
                catch (Exception e)
                {
                    outcomes = new List<GitHubIssues.Outcome> { new GitHubIssues.Outcome { Folder = mine, Error = e.Message } };
                }
                _fromFiling.Enqueue(() => Filed(outcomes, mine));
            });
        }

        void Filed(List<GitHubIssues.Outcome> outcomes, string mine)
        {
            _filing = false;
            _progress = null;
            foreach (GitHubIssues.Outcome outcome in outcomes)
                if (outcome.Filed) Debug.Log($"[Feedback] Filed {Path.GetFileName(outcome.Folder)} as {outcome.Url}");
                else Debug.LogWarning($"[Feedback] Didn't file {Path.GetFileName(outcome.Folder)}: {outcome.Error}");
            GitHubIssues.Outcome own = outcomes.FirstOrDefault(o => o.Folder == mine);
            string line = own == null ? "It was filed already."
                : own.Filed ? $"Filed on GitHub as {own.Url}"
                : $"Not filed on GitHub: {own.Error}. It's kept, and goes with the next ticket you send.";
            if (own?.Filed == true) _issueUrl = own.Url;
            int earlier = outcomes.Count(o => o.Folder != mine && o.Filed);
            if (earlier > 0) line += $" Also filed {earlier} earlier ticket{(earlier == 1 ? "" : "s")}.";
            if (_state == State.Sent && _folder == mine) _status = $"Saved as ticket {Path.GetFileName(mine)}. {line}";
        }

        /// <summary>The form is showing (being written, or sent).</summary>
        public bool IsOpen => _state == State.Writing || _state == State.Sent;

        public void Close()
        {
            if (_state == State.Writing || _state == State.Sent) Resume();
            DropScreenshot();
            _ticket = null;
            _state = State.Closed;
        }

        void DropScreenshot()
        {
            if (_shot != null) Destroy(_shot);
            _shot = null;
        }

        /// <summary>Stops the game and its UI underneath while the form is open.</summary>
        void Pause()
        {
            _timeScale = Time.timeScale;
            Time.timeScale = 0f;
            foreach (UIDocument document in FindObjectsByType<UIDocument>(FindObjectsSortMode.None))
            {
                VisualElement root = document.isActiveAndEnabled ? document.rootVisualElement : null;
                if (root == null || !root.enabledSelf) continue;
                root.SetEnabled(false);
                _paused.Add(root);
            }
        }

        void Resume()
        {
            Time.timeScale = _timeScale;
            foreach (VisualElement root in _paused) root.SetEnabled(true);
            _paused.Clear();
        }

        // ---------------------------------------------------------------- the form

        void OnGUI()
        {
            // Readable on big screens: lay out at 900 px tall or more.
            float scale = Mathf.Max(1f, Screen.height / 900f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            float width = Screen.width / scale, height = Screen.height / scale;

            if (_state == State.Closed)
            {
                if (_showButton && GUI.Button(new Rect(8f, height - 30f, 116f, 22f), "Feedback (F8)")) Open();
                return;
            }
            if (_state == State.Capturing) return;

            GUI.color = new Color(0f, 0f, 0f, 0.45f);
            GUI.DrawTexture(new Rect(0f, 0f, width, height), Texture2D.whiteTexture);
            GUI.color = Color.white;
            float w = Mathf.Min(640f, width - 40f), h = Mathf.Min(_state == State.Sent ? 240f : 560f, height - 40f);
            GUI.ModalWindow(WindowId, new Rect((width - w) / 2f, (height - h) / 2f, w, h), Form, "Feedback", WindowStyle());
        }

        GUIStyle _window;
        Texture2D _windowBackground;

        /// <summary>The default window, but solid: the game doesn't show through what's being written.</summary>
        GUIStyle WindowStyle()
        {
            if (_window != null) return _window;
            _windowBackground = new Texture2D(1, 1);
            _windowBackground.SetPixel(0, 0, new Color(0.15f, 0.15f, 0.17f, 1f));
            _windowBackground.Apply();
            _window = new GUIStyle(GUI.skin.window) { padding = new RectOffset(12, 12, 26, 12) };
            _window.normal.background = _window.onNormal.background = _windowBackground;
            return _window;
        }

        void Form(int id)
        {
            Event e = Event.current;
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            {
                Close();
                e.Use();
                return;
            }
            if (_state == State.Sent)
            {
                SentForm();
                return;
            }
            bool canSend = _title.Trim().Length > 0 || _description.Trim().Length > 0;
            if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) && (e.control || e.command) && canSend)
            {
                e.Use();
                Send();
                return;
            }

            GUILayout.Label("Title");
            GUI.SetNextControlName("feedback-title");
            _title = GUILayout.TextField(_title, 120);
            GUILayout.Space(6f);
            _category = GUILayout.Toolbar(_category, FeedbackTicket.Categories);
            GUILayout.Space(6f);
            GUILayout.Label("What happened, and what did you expect? Or, what's the idea?");
            _description = GUILayout.TextArea(_description, GUILayout.ExpandHeight(true), GUILayout.MinHeight(100f));
            GUILayout.Space(6f);

            GUILayout.BeginHorizontal();
            if (_shot != null)
            {
                float thumbHeight = 110f, thumbWidth = thumbHeight * _shot.width / Mathf.Max(1, _shot.height);
                Rect thumb = GUILayoutUtility.GetRect(thumbWidth, thumbHeight, GUILayout.Width(thumbWidth), GUILayout.Height(thumbHeight));
                GUI.color = _includeShot ? Color.white : new Color(1f, 1f, 1f, 0.25f);
                GUI.DrawTexture(thumb, _shot, ScaleMode.ScaleToFit);
                GUI.color = Color.white;
                GUILayout.Space(8f);
            }
            GUILayout.BeginVertical();
            if (_shot != null) _includeShot = GUILayout.Toggle(_includeShot, " Include the screenshot");
            GUILayout.Label(WhatGoes());
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();

            if (_status != null) GUILayout.Label(_status);
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Cancel", GUILayout.Width(100f))) Close();
            GUI.enabled = canSend;
            if (GUILayout.Button("Send  (Ctrl+Enter)", GUILayout.Width(150f))) Send();
            GUI.enabled = true;
            GUILayout.EndHorizontal();

            if (_focusTitle && e.type == EventType.Repaint)
            {
                GUI.FocusControl("feedback-title");
                _focusTitle = false;
            }
        }

        void SentForm()
        {
            GUILayout.Label(_status);
            if (_progress != null) GUILayout.Label(_progress);
            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (_issueUrl != null && GUILayout.Button("Open the issue", GUILayout.Width(130f))) Application.OpenURL(_issueUrl);
            if (_folder != null && GUILayout.Button("Show the ticket", GUILayout.Width(130f))) Application.OpenURL(new Uri(_folder).AbsoluteUri);
            if (GUILayout.Button("Close", GUILayout.Width(100f))) Close();
            GUILayout.EndHorizontal();
        }

        string WhatGoes()
        {
            int people = _ticket?.OnScreen.Count ?? 0;
            string attached = _ticket != null && _ticket.Attachments.Count > 0 ? ", " + string.Join(", ", _ticket.Attachments.Keys) : "";
            return $"Also sent: the game state ({people} {(people == 1 ? "person" : "people")} on screen, with their looks and every material's settings), " +
                   $"the camera and lights, the screen's text and the recent log{attached}. Saved in {FeedbackPaths.FolderName}/ and " +
                   (FilingEnabled ? "filed as a GitHub issue, which anyone who can see the repository can read." : "not filed (-feedback-no-file).");
        }
    }
}
