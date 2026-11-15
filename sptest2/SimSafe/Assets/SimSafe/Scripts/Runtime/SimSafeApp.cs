using System.Collections;
using System.Collections.Generic;
using System.Linq;
using SimSafe.Core;
using UnityEngine;
using UnityEngine.EventSystems;

namespace SimSafe
{
    /// <summary>Creates SimSafe at runtime in whatever scene is open, so the project needs no hand-built scene.</summary>
    public static class Bootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init()
        {
            if (Object.FindFirstObjectByType<SimSafeApp>() != null) return;
            new GameObject("SimSafe").AddComponent<SimSafeApp>();
        }
    }

    /// <summary>
    /// Top-level controller: app states (menu, briefing, playing, dialogue, paused, debrief) and the glue between
    /// the pure ScenarioSession, the 3D world and the UI. Scenario rules live in SimSafe.Core, not here.
    /// </summary>
    public sealed class SimSafeApp : MonoBehaviour
    {
        enum Mode { Menu, Briefing, Playing, Dialogue, Paused, Debrief }

        Mode mode, modeBeforePause;
        UiManager ui;
        WorldRig world;
        PlayerController player;
        Camera cam;
        readonly List<ScenarioDefinition> scenarios = new List<ScenarioDefinition>();
        ScenarioDefinition selected;
        ScenarioSession session;
        Coroutine flow;
        float menuTime;
        ScenarioEventMonitor eventMonitor;
        SimulationEventRecorder eventRecorder;
        ISimulationClock simulationClock;
        OfflineCharacterConversation samConversation;
        OfflineVoiceController offlineVoice;
        long lastResponseRevision;
        long lastVoiceResponseRevision = -1;
        int resumeInputAfterFrame = -1;
        float captionRefreshAt, speechCaptionExpiresAt;

        public ScenarioSession CurrentSession { get { return session; } }
        public IReadOnlyList<SimulationEvent> MonitoringEvents => eventMonitor != null
            ? eventMonitor.Snapshot() : System.Array.AsReadOnly(new SimulationEvent[0]);

        // ------------------------------------------------------------------
        // Startup
        // ------------------------------------------------------------------

        void Awake()
        {
            simulationClock = new StopwatchSimulationClock();
            eventRecorder = new SimulationEventRecorder(simulationClock);
            eventMonitor = new ScenarioEventMonitor(eventRecorder);
            Application.targetFrameRate = 60;
            GameSettings.Apply();
            IsolateFromSceneContent();

            var problems = new List<string>();
            scenarios.AddRange(ScenarioLibrary.LoadAll(problems));
            foreach (var p in problems) Debug.LogError("[SimSafe] Scenario problem: " + p);
            selected = scenarios.FirstOrDefault();

            world = WorldBuilder.Build();
            player = PlayerController.Create();

            var camGo = new GameObject("SimSafe Camera") { tag = "MainCamera" };
            cam = camGo.AddComponent<Camera>();
            camGo.AddComponent<AudioListener>();
            cam.fieldOfView = 68f; cam.nearClipPlane = 0.1f; cam.farClipPlane = 250f; cam.allowHDR = false;
            cam.clearFlags = RenderSettings.skybox != null ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.62f, 0.78f, 0.95f);

            ui = new GameObject("SimSafe UI").AddComponent<UiManager>();
            ui.Init(this);
            offlineVoice = gameObject.AddComponent<OfflineVoiceController>();
            offlineVoice.Initialize(world.Sam.transform, eventRecorder, simulationClock);
            offlineVoice.TranscriptReady += text =>
            {
                if (mode == Mode.Playing && ui.ConversationInputOpen && samConversation != null && samConversation.CanSubmit)
                    ui.ApplyVoiceTranscript(text);
            };
            ui.ConfigureVoice(offlineVoice.ToggleMicrophone, offlineVoice.SetOutputEnabled, offlineVoice.IsAvailable);
            GoToMenu();
        }

        /// <summary>If the project's template scene has its own camera/lights, switch them off so ours are the only ones.</summary>
        static void IsolateFromSceneContent()
        {
            foreach (var c in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None)) c.gameObject.SetActive(false);
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None)) l.gameObject.SetActive(false);
        }

        // ------------------------------------------------------------------
        // Navigation (called by the UI)
        // ------------------------------------------------------------------

        public void GoToMenu()
        {
            StopFlow();
            ClearOfflineConversation();
            eventMonitor?.End(SessionEndReason.ReturnedToMenu);
            Time.timeScale = 1f; AudioListener.pause = false;
            mode = Mode.Menu;
            player.Active = false;
            SetCursor(false);
            cam.transform.SetParent(null, true);
            world.SetIdleDisplay();
            session = null;
            ui.SetPrompt(null);
            ui.ShowMainMenu(selected);
        }

        public void OpenScenarioSelect() { mode = Mode.Menu; ui.ShowScenarioSelect(scenarios, selected); }

        public void SelectScenario(ScenarioDefinition def) { selected = def; }

        public void OpenBriefing()
        {
            if (selected == null) { Debug.LogError("[SimSafe] No valid scenario loaded."); return; }
            mode = Mode.Briefing;
            ui.ShowBriefing(selected);
        }

        public void OpenSettings(System.Action back) { ui.ShowSettings(back); }

        public void ReturnToPause() { ui.ShowPause(); }

        public void QuitApp()
        {
            ClearOfflineConversation();
            eventMonitor?.End(SessionEndReason.ApplicationStopped);
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        /// <summary>Starts (or restarts / replays) the selected scenario from the beginning.</summary>
        public void BeginScenario()
        {
            StopFlow();
            ClearOfflineConversation();
            eventMonitor?.End(SessionEndReason.Restarted);
            Time.timeScale = 1f; AudioListener.pause = false;

            session = new ScenarioSession(selected);
            session.Changed += OnSessionChanged;
            world.ResetForRun(player.transform);
            player.Teleport(world.PlayerSpawn.position, world.PlayerSpawnYaw);

            cam.transform.SetParent(player.CameraMount, false);
            cam.transform.localPosition = Vector3.zero;
            cam.transform.localRotation = Quaternion.identity;

            ui.HideDialogue();
            ui.SetHint("");
            session.Start();
            eventMonitor.Start(session);
            // Explicit KAI mapping: this scenario's dispatch discloses autism. Never infer from text.
            samConversation = !session.IsFinished && selected.id == "residential-welfare-call"
                ? new OfflineCharacterConversation(new CharacterProfile("sam", 12, 2,
                    "I can listen now. Please tell me one thing at a time.",
                    "It is hard to listen with that noise. Please lower it.",
                    "Please give me some more space."), ScenarioCategory.KAI, eventRecorder, simulationClock)
                : null;
            lastResponseRevision = 0;
            lastVoiceResponseRevision = -1;
            ui.Only("hud");
            ui.RefreshHud(session);
            AdvanceFlow();
        }

        void OnSessionChanged()
        {
            if (session == null) return;
            if (session.IsFinished) ClearOfflineConversation();
            world.Sync(session);
            ui.RefreshHud(session);
        }

        // ------------------------------------------------------------------
        // Scenario flow
        // ------------------------------------------------------------------

        void StopFlow()
        {
            if (flow != null) { StopCoroutine(flow); flow = null; }
        }

        void AdvanceFlow()
        {
            StopFlow();
            var node = session.CurrentNode;
            if (node.kind == "outcome")
            {
                mode = Mode.Dialogue; player.Active = false; SetCursor(false);
                ui.ShowOutcome(node, ShowDebrief);
                return;
            }
            if (node.trigger == "interact")
            {
                // Free roam until the officer chooses to engage (E on Sam or Dana).
                ui.HideDialogue();
                ui.Only("hud");
                ui.SetHint(node.hint);
                mode = Mode.Playing; player.Active = true; SetCursor(true);
                return;
            }
            flow = StartCoroutine(OpenNodeRoutine(node));
        }

        IEnumerator OpenNodeRoutine(ScenarioNode node)
        {
            CancelOfflineTurn();
            mode = Mode.Dialogue; player.Active = false; SetCursor(false);
            ui.SetHint(""); ui.SetPrompt(null); ui.HideDialogue();
            ui.Only("hud");

            if (node.stage == "near_sam")
            {
                // A slow, deliberate walk to a respectful distance rather than a teleport.
                while (!player.WalkToward(world.Sam.transform.position, 4.5f, 2.0f, Time.deltaTime)) yield return null;
            }
            else if (node.stage == "dana_comes")
            {
                world.DanaMeet(player.transform.position);
                float guard = 0f;
                yield return null;
                while (world.Dana.IsMoving && guard < 8f) { guard += Time.deltaTime; yield return null; }
            }

            var choices = session.GetAvailableOptions()
                .Select(o => { var opt = o; return new Choice { Label = opt.text, OnSelect = () => OnChoose(opt) }; })
                .ToList();
            ui.ShowDialogue(node.speakerName, node.text, choices);
            flow = null;
        }

        void OnChoose(DecisionOption option)
        {
            StopFlow();
            flow = StartCoroutine(ChooseRoutine(option));
        }

        IEnumerator ChooseRoutine(DecisionOption option)
        {
            if (option.waitSeconds > 0.01f)
            {
                bool waited = false;
                ui.ShowWait(session.CurrentNode.speakerName, option.waitSeconds * GameSettings.WaitScale, () => waited = true);
                while (!waited) yield return null;
            }

            DecisionRecord rec = session.Choose(option.id);

            if (!string.IsNullOrEmpty(rec.Feedback) || !string.IsNullOrEmpty(option.response))
            {
                bool proceed = false;
                ui.ShowFeedback(rec, option.response, () => proceed = true);
                while (!proceed) yield return null;
            }
            flow = null;
            AdvanceFlow();
        }

        void ShowDebrief()
        {
            mode = Mode.Debrief; player.Active = false; SetCursor(false);
            ui.HideDialogue(); ui.SetPrompt(null);
            ui.ShowDebrief(DebriefBuilder.Build(session));
        }

        // ------------------------------------------------------------------
        // Pause
        // ------------------------------------------------------------------

        void Pause()
        {
            if (mode != Mode.Playing && mode != Mode.Dialogue) return;
            ui.HideConversationInput();
            offlineVoice?.SetPaused(true);
            UpdateOfflineConversation(true);
            modeBeforePause = mode; mode = Mode.Paused;
            Time.timeScale = 0f; AudioListener.pause = true;
            player.Active = false; SetCursor(false);
            ui.SetPrompt(null);
            ui.ShowPause();
        }

        public void Resume()
        {
            if (mode != Mode.Paused) return;
            Time.timeScale = 1f; AudioListener.pause = false;
            mode = modeBeforePause;
            offlineVoice?.SetPaused(false);
            UpdateOfflineConversation(mode != Mode.Playing);
            if (ui.DialogueActive) ui.Only("hud", "dialogue"); else ui.Only("hud");
            if (mode == Mode.Playing) { player.Active = true; SetCursor(true); }
        }

        // ------------------------------------------------------------------
        // Per-frame
        // ------------------------------------------------------------------

        void Update()
        {
            switch (mode)
            {
                case Mode.Menu:
                case Mode.Briefing:
                    menuTime += Time.unscaledDeltaTime;
                    cam.transform.position = new Vector3(Mathf.Sin(menuTime * 0.08f) * 9f - 1f, 3.4f, -21f);
                    cam.transform.LookAt(new Vector3(1f, 2.2f, 8f));
                    break;

                case Mode.Playing:
                    UpdateOfflineConversation(false);
                    // Typing owns all keys, including E/P and numeric dialogue shortcuts.
                    if (ui.ConversationInputConsumedThisFrame || Time.frameCount <= resumeInputAfterFrame) break;
                    if (PausePressed()) { Pause(); break; }
                    // WebGL needs a click to (re)capture the mouse.
                    if (Cursor.lockState != CursorLockMode.Locked && Input.GetMouseButtonDown(0) && !PointerOverUi()) SetCursor(true);
                    UpdateInteraction();
                    break;

                case Mode.Dialogue:
                    if (PausePressed()) Pause();
                    break;

                case Mode.Paused:
                    UpdateOfflineConversation(true);
                    if (PausePressed()) Resume();
                    break;
            }
        }

        static bool PausePressed() { return Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.P); }

        static bool PointerOverUi() { return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject(); }

        void UpdateInteraction()
        {
            var focus = player.Focus;
            string prompt = focus != null ? PromptFor(focus) : null;
            if (Cursor.lockState != CursorLockMode.Locked) prompt = prompt ?? "Click to resume mouse look";
            ui.SetPrompt(prompt);
            if (focus != null && PromptFor(focus) != null)
            {
                if (IsOfflineSam(focus) && Input.GetKeyDown(KeyCode.F))
                {
                    StopFlow(); flow = StartCoroutine(OpenNodeRoutine(session.CurrentNode));
                }
                else if (Input.GetKeyDown(KeyCode.E)) HandleInteract(focus);
            }
        }

        string PromptFor(Interactable i)
        {
            if (session == null || session.IsFinished) return null;
            if (i.Kind == InteractKind.Character)
            {
                if (IsOfflineSam(i))
                    return session.CurrentNode.trigger == "interact" && samConversation.CanSubmit
                        ? "[E] Talk with Sam (text)   [F] Scenario choices" : null;
                return session.CurrentNode.trigger == "interact" ? "[E]  Speak with " + i.DisplayName : null;
            }
            string id = i.InteractionId != null ? i.InteractionId(session) : null;
            if (id == null || !session.CanInteract(id)) return null;
            return "[E]  " + session.GetInteraction(id).prompt;
        }

        void HandleInteract(Interactable i)
        {
            if (i.Kind == InteractKind.Character)
            {
                if (IsOfflineSam(i)) { OpenSamTextInput(); return; }
                StopFlow();
                flow = StartCoroutine(OpenNodeRoutine(session.CurrentNode));
                return;
            }
            string id = i.InteractionId != null ? i.InteractionId(session) : null;
            var rec = id != null ? session.Interact(id) : null;
            if (rec != null)
            {
                eventMonitor.ObserveInteraction(rec);
                ui.Toast(rec);
            }
        }

        bool IsOfflineSam(Interactable interactable) => samConversation != null &&
            interactable.Kind == InteractKind.Character && interactable.CharacterId == "sam";

        double DistanceToSam()
        {
            Vector3 distance = player.transform.position - world.Sam.transform.position;
            distance.y = 0f;
            return distance.magnitude;
        }

        void OpenSamTextInput()
        {
            if (mode != Mode.Playing || samConversation == null || !samConversation.CanSubmit) return;
            player.Active = false; SetCursor(false); ui.SetPrompt(null);
            ui.ShowConversationInput("Sam", text =>
            {
                // Recheck live distance/conditions: Sam may move while the officer types.
                UpdateOfflineConversation(false);
                if (mode == Mode.Playing && samConversation != null && samConversation.CanSubmit)
                {
                    var turnId = samConversation.Submit(text);
                    offlineVoice?.ConfirmTranscript(turnId);
                    RefreshSamCaption();
                }
                ReturnFromSamTextInput();
            }, ReturnFromSamTextInput);
        }

        void ReturnFromSamTextInput()
        {
            offlineVoice?.CancelCapture();
            if (mode != Mode.Playing) return;
            resumeInputAfterFrame = Time.frameCount + 1;
            player.SuppressInputThroughFrame = resumeInputAfterFrame;
            player.Active = true; SetCursor(true);
        }

        void UpdateOfflineConversation(bool paused)
        {
            if (samConversation == null || session == null || session.IsFinished) return;
            samConversation.Observe(DistanceToSam(), !session.HasFlag("sirenOff"), !session.HasFlag("radioLow"), paused);
            ui.SetVoiceState(offlineVoice.Status, offlineVoice.IsRecording,
                offlineVoice.IsTranscribing || offlineVoice.IsRequestingPermission, offlineVoice.IsAvailable);
            if (!paused)
            {
                if (ui.ConversationInputOpen && !samConversation.CanSubmit)
                {
                    ui.HideConversationInput(); ReturnFromSamTextInput();
                }
                RefreshSamCaption();
                ui.PositionCharacterResponse(cam, world.Sam.transform.position + Vector3.up * 2.05f);
            }
        }

        void RefreshSamCaption()
        {
            if (samConversation == null) return;
            var response = samConversation.CurrentResponse;
            if (samConversation.ResponseRevision != lastVoiceResponseRevision)
            {
                lastVoiceResponseRevision = samConversation.ResponseRevision;
                offlineVoice?.Present(response);
            }
            if (response == null) { ui.ClearCharacterResponse(); return; }
            if (samConversation.ResponseRevision != lastResponseRevision)
                speechCaptionExpiresAt = response.Type == ResponseIntentionType.Speak
                    ? Time.unscaledTime + 6f : float.PositiveInfinity;
            if (Time.unscaledTime >= speechCaptionExpiresAt && (offlineVoice == null || !offlineVoice.IsSpeaking))
            { ui.ClearCharacterResponse(); return; }
            if (samConversation.ResponseRevision == lastResponseRevision && Time.unscaledTime < captionRefreshAt) return;
            lastResponseRevision = samConversation.ResponseRevision;
            captionRefreshAt = Time.unscaledTime + 0.1f;
            string status = response.Type == ResponseIntentionType.Wait
                ? "WAIT - processing: " + samConversation.RemainingWaitSeconds.ToString("0.0") + " s"
                : response.Type == ResponseIntentionType.RequestSpace ? "REQUEST SPACE" : "SPEAK";
            string text = response.Type == ResponseIntentionType.Wait ? "Sam is taking time to process." : response.Text;
            ui.SetCharacterResponse("Sam", text, status);
        }

        void CancelOfflineTurn()
        {
            offlineVoice?.CancelAll();
            samConversation?.Cancel();
            ui?.HideConversationInput(); ui?.ClearCharacterResponse();
        }

        void ClearOfflineConversation()
        {
            CancelOfflineTurn(); samConversation = null;
        }

        void OnApplicationFocus(bool focused)
        {
            if (!focused && ui != null && (mode == Mode.Playing || mode == Mode.Dialogue)) Pause();
        }

        void OnDestroy()
        {
            ClearOfflineConversation(); eventMonitor?.End(SessionEndReason.ApplicationStopped);
        }

        static void SetCursor(bool gameplay)
        {
            Cursor.lockState = gameplay ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !gameplay;
        }
    }
}
