using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using SimSafe.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SimSafe
{
    public sealed class Choice
    {
        public string Label;
        public Action OnSelect;
    }

    /// <summary>
    /// Builds and drives every screen in code (uGUI): main menu, scenario selection, dispatch briefing,
    /// HUD, dialogue/feedback panels, pause, settings and the training debrief.
    /// Accessibility: everything is keyboard-operable (Tab/arrows + Enter, number keys for choices),
    /// the interface text size is adjustable, and state is never conveyed by colour alone.
    /// </summary>
    public sealed class UiManager : MonoBehaviour
    {
        SimSafeApp app;
        Canvas canvas;
        CanvasScaler scaler;
        readonly Dictionary<string, CanvasGroup> screens = new Dictionary<string, CanvasGroup>();

        // menu / select / briefing / pause / settings / debrief
        Text menuScenarioLabel;
        RectTransform selectList, briefingBody, settingsBody, debriefContent;
        ScrollRect debriefScroll;
        Action settingsBack;

        // HUD
        Text objectivesText, hintText, indicatorsText, promptText, toastText;
        GameObject promptBox, toastBox, indicatorsBox, hintBox;
        float toastUntil;

        // Offline in-scene conversation. These controls share the existing HUD/EventSystem.
        RectTransform conversationPanel, characterResponsePanel;
        InputField conversationInput;
        Button conversationSend, conversationClose;
        Button conversationMicrophone;
        Toggle samVoiceToggle;
        Text voiceStatusText;
        Action recordMicrophone;
        Text conversationTitle, characterNameText, characterResponseText, characterStatusText;
        Action<string> submitConversation;
        Action cancelConversation;
        Coroutine focusConversation;
        bool characterResponseAvailable;
        int conversationOpenedFrame, conversationClosedFrame = -1;
        public bool ConversationInputOpen => conversationPanel != null && conversationPanel.gameObject.activeInHierarchy;
        public bool ConversationInputConsumedThisFrame => ConversationInputOpen || conversationClosedFrame == Time.frameCount;

        // dialogue
        RectTransform dialoguePanel;
        readonly List<Action> numberActions = new List<Action>();
        bool choicesLocked;
        Coroutine waitRoutine;
        public bool DialogueActive { get; private set; }

        // ------------------------------------------------------------------
        // Setup
        // ------------------------------------------------------------------

        public void Init(SimSafeApp owner)
        {
            app = owner;
            var eventSystem = EventSystem.current != null ? EventSystem.current : FindFirstObjectByType<EventSystem>();
            if (eventSystem == null) eventSystem = new GameObject("EventSystem", typeof(EventSystem)).GetComponent<EventSystem>();
            // Preserve an existing enabled module (including the new Input System module).
            // A second enabled module would compete for submit/navigation events.
            if (!eventSystem.GetComponents<BaseInputModule>().Any(module => module.isActiveAndEnabled))
            {
                var legacyModule = eventSystem.GetComponent<StandaloneInputModule>();
                if (legacyModule != null) legacyModule.enabled = true;
                else eventSystem.gameObject.AddComponent<StandaloneInputModule>();
            }

            var cgo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            cgo.transform.SetParent(transform, false);
            canvas = cgo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            scaler = cgo.GetComponent<CanvasScaler>();
            ApplyScale();

            BuildMenu(); BuildSelect(); BuildBriefing(); BuildHud(); BuildDialogue(); BuildPause(); BuildSettings(); BuildDebrief();
        }

        public void ApplyScale()
        {
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f) / GameSettings.UiScale;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
        }

        RectTransform NewScreen(string name)
        {
            var r = UIK.Rect(name, canvas.transform);
            UIK.Stretch(r);
            screens[name] = r.gameObject.AddComponent<CanvasGroup>();
            r.gameObject.SetActive(false);
            return r;
        }

        /// <summary>Shows exactly these screens (fading in any that were hidden).</summary>
        public void Only(params string[] names)
        {
            if (!names.Contains("hud") || names.Contains("dialogue")) HideConversationInput();
            foreach (var kv in screens)
            {
                bool on = names.Contains(kv.Key);
                var go = kv.Value.gameObject;
                if (on && !go.activeSelf) { go.SetActive(true); StartCoroutine(Fade(kv.Value)); }
                else if (!on && go.activeSelf) go.SetActive(false);
            }
        }

        IEnumerator Fade(CanvasGroup cg)
        {
            float t = 0f;
            cg.alpha = 0f;
            while (t < 0.22f && cg != null)
            {
                t += Time.unscaledDeltaTime;
                cg.alpha = Mathf.Clamp01(t / 0.22f);
                yield return null;
            }
            if (cg != null) cg.alpha = 1f;
        }

        static void Focus(Selectable s)
        {
            if (s != null && EventSystem.current != null) EventSystem.current.SetSelectedGameObject(s.gameObject);
        }

        static void Clear(Transform t)
        {
            foreach (Transform c in t) { c.gameObject.SetActive(false); Destroy(c.gameObject); }
        }

        static RectTransform CentrePanel(Transform parent, float width, bool dim)
        {
            if (dim) { var d = UIK.Box(parent, "Dim", UIK.Dim); UIK.Stretch(d.rectTransform); }
            var p = UIK.Box(parent, "Panel", UIK.Panel).rectTransform;
            p.anchorMin = p.anchorMax = p.pivot = new Vector2(0.5f, 0.5f);
            p.sizeDelta = new Vector2(width, 0f);
            p.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            UIK.VStack(p.gameObject, 48, 40, 16);
            return p;
        }

        static void Spacer(Transform parent, float height, bool flexible = false)
        {
            var r = UIK.Rect("Spacer", parent);
            var le = r.gameObject.AddComponent<LayoutElement>();
            le.minHeight = height; if (flexible) le.flexibleHeight = 1f;
        }

        // ------------------------------------------------------------------
        // Main menu / selection / briefing
        // ------------------------------------------------------------------

        void BuildMenu()
        {
            var s = NewScreen("menu");
            var left = UIK.Box(s, "Side", new Color(0.04f, 0.07f, 0.12f, 0.90f)).rectTransform;
            left.anchorMin = new Vector2(0f, 0f); left.anchorMax = new Vector2(0f, 1f); left.pivot = new Vector2(0f, 0.5f);
            left.sizeDelta = new Vector2(740f, 0f);
            UIK.VStack(left.gameObject, 72, 80, 16);

            UIK.Label(left, "Sim" + UIK.Tint("Safe", UIK.Accent), 104, UIK.Text, FontStyle.Bold);
            UIK.Label(left, "Autism-aware encounter training", 32, UIK.Text);
            UIK.Label(left, "Prototype for National Autism Safety Council review", 24, UIK.Muted);
            Spacer(left, 36);
            UIK.MakeButton(left, "Start Training", () => app.OpenBriefing(), 32, true, 72);
            UIK.MakeButton(left, "Scenario Selection", () => app.OpenScenarioSelect(), 30);
            UIK.MakeButton(left, "Settings", () => app.OpenSettings(() => app.GoToMenu()), 30);
#if !UNITY_WEBGL || UNITY_EDITOR
            UIK.MakeButton(left, "Quit", () => app.QuitApp(), 30);
#endif
            Spacer(left, 20);
            menuScenarioLabel = UIK.Label(left, "", 24, UIK.Muted);
            Spacer(left, 0, true);
            UIK.Label(left, "Illustrative content only. Not yet reviewed or approved by NASC. Does not measure real-world officer competence.", 20, UIK.Warn);
        }

        public void ShowMainMenu(ScenarioDefinition selected)
        {
            menuScenarioLabel.text = selected == null ? "No valid scenario found" : "Scenario: " + selected.title;
            Only("menu");
            var first = screens["menu"].GetComponentInChildren<Button>();
            Focus(first);
        }

        void BuildSelect() { var s = NewScreen("select"); var p = CentrePanel(s, 1200f, true); selectList = p; }

        public void ShowScenarioSelect(IList<ScenarioDefinition> list, ScenarioDefinition selected)
        {
            Clear(selectList);
            UIK.Label(selectList, "SCENARIO SELECTION", 24, UIK.Accent, FontStyle.Bold);
            UIK.Label(selectList, "Choose a training scenario", 44, UIK.Text, FontStyle.Bold);
            Button first = null;
            foreach (var sc in list)
            {
                var def = sc;
                string mark = def == selected ? UIK.Tint("SELECTED  ", UIK.Good) : "";
                var b = UIK.MakeButton(selectList, mark + "<b>" + def.title + "</b>\n" + def.summary + "\n" + UIK.Tint(def.status, UIK.Warn), () => { app.SelectScenario(def); ShowScenarioSelect(list, def); }, 26);
                if (first == null) first = b;
            }
            UIK.Label(selectList, "More scenarios will be added after NASC review.", 22, UIK.Muted);
            UIK.MakeButton(selectList, "Back", () => app.GoToMenu(), 28);
            Only("select");
            Focus(first);
        }

        void BuildBriefing() { var s = NewScreen("briefing"); briefingBody = CentrePanel(s, 1200f, true); }

        public void ShowBriefing(ScenarioDefinition sc)
        {
            Clear(briefingBody);
            UIK.Label(briefingBody, "DISPATCH BRIEFING", 24, UIK.Accent, FontStyle.Bold);
            UIK.Label(briefingBody, sc.title, 44, UIK.Text, FontStyle.Bold);
            var box = UIK.Box(briefingBody, "Dispatch", new Color(0.02f, 0.05f, 0.09f, 1f));
            UIK.VStack(box.gameObject, 28, 24, 0);
            UIK.Label(box.transform, sc.dispatchBriefing, 29, new Color(0.85f, 0.95f, 0.88f));
            UIK.Label(briefingBody, "Controls: WASD move, mouse look, E interact, Esc or P pause. Choices can also be picked with number keys.", 22, UIK.Muted);
            var go = UIK.MakeButton(briefingBody, "Begin Scenario", () => app.BeginScenario(), 32, true, 68);
            UIK.MakeButton(briefingBody, "Back", () => app.GoToMenu(), 28);
            Only("briefing");
            Focus(go);
        }

        // ------------------------------------------------------------------
        // HUD
        // ------------------------------------------------------------------

        void BuildHud()
        {
            var s = NewScreen("hud");

            var obj = UIK.Box(s, "Objectives", new Color(0.05f, 0.09f, 0.15f, 0.82f)).rectTransform;
            obj.anchorMin = obj.anchorMax = obj.pivot = new Vector2(0f, 1f);
            obj.anchoredPosition = new Vector2(28f, -28f); obj.sizeDelta = new Vector2(560f, 0f);
            obj.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            UIK.VStack(obj.gameObject, 22, 18, 8);
            UIK.Label(obj, "OBJECTIVES", 20, UIK.Accent, FontStyle.Bold);
            objectivesText = UIK.Label(obj, "", 24, UIK.Text);
            hintBox = UIK.Box(obj, "HintBox", Color.clear).gameObject;
            UIK.VStack(hintBox, 0, 6, 0);
            hintText = UIK.Label(hintBox.transform, "", 21, UIK.Warn);
            UIK.Label(obj, "Esc / P  pause", 18, UIK.Muted);

            var ind = UIK.Box(s, "Indicators", new Color(0.05f, 0.09f, 0.15f, 0.82f)).rectTransform;
            ind.anchorMin = ind.anchorMax = ind.pivot = new Vector2(1f, 1f);
            ind.anchoredPosition = new Vector2(-28f, -28f); ind.sizeDelta = new Vector2(440f, 0f);
            ind.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            UIK.VStack(ind.gameObject, 22, 18, 6);
            UIK.Label(ind, "SCENE INDICATORS (training aid)", 18, UIK.Accent, FontStyle.Bold);
            indicatorsText = UIK.Label(ind, "", 24, UIK.Text);
            indicatorsBox = ind.gameObject;

            var dot = UIK.Box(s, "Crosshair", new Color(1f, 1f, 1f, 0.8f)).rectTransform;
            dot.anchorMin = dot.anchorMax = dot.pivot = new Vector2(0.5f, 0.5f); dot.sizeDelta = new Vector2(6f, 6f);

            var pr = UIK.Box(s, "Prompt", new Color(0.05f, 0.09f, 0.15f, 0.9f)).rectTransform;
            pr.anchorMin = pr.anchorMax = new Vector2(0.5f, 0f); pr.pivot = new Vector2(0.5f, 0f);
            pr.anchoredPosition = new Vector2(0f, 120f); pr.sizeDelta = new Vector2(0f, 0f);
            var fit = pr.gameObject.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize; fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            UIK.VStack(pr.gameObject, 28, 14, 0);
            promptText = UIK.Label(pr, "", 30, UIK.Text, FontStyle.Bold, TextAnchor.MiddleCenter);
            promptBox = pr.gameObject; promptBox.SetActive(false);

            var to = UIK.Box(s, "Toast", new Color(0.05f, 0.12f, 0.17f, 0.95f)).rectTransform;
            to.anchorMin = to.anchorMax = to.pivot = new Vector2(0.5f, 1f);
            to.anchoredPosition = new Vector2(0f, -28f); to.sizeDelta = new Vector2(900f, 0f);
            to.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            UIK.VStack(to.gameObject, 26, 18, 0);
            toastText = UIK.Label(to, "", 24, UIK.Text);
            toastBox = to.gameObject; toastBox.SetActive(false);
            // HUD decorations must not intercept pointer recapture at the crosshair.
            foreach (var graphic in s.GetComponentsInChildren<Graphic>(true)) graphic.raycastTarget = false;
            BuildCharacterConversation(s);
        }

        void BuildCharacterConversation(Transform hud)
        {
            characterResponsePanel = UIK.Box(hud, "CharacterResponse", UIK.Panel).rectTransform;
            characterResponsePanel.anchorMin = characterResponsePanel.anchorMax = new Vector2(0.5f, 0.5f);
            characterResponsePanel.pivot = new Vector2(0.5f, 0f);
            characterResponsePanel.sizeDelta = new Vector2(620f, 0f);
            characterResponsePanel.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            UIK.VStack(characterResponsePanel.gameObject, 24, 16, 6);
            characterNameText = UIK.Label(characterResponsePanel, "", 22, UIK.Accent, FontStyle.Bold);
            characterResponseText = UIK.Label(characterResponsePanel, "", 28, UIK.Text);
            characterStatusText = UIK.Label(characterResponsePanel, "", 20, UIK.Muted);
            foreach (var graphic in characterResponsePanel.GetComponentsInChildren<Graphic>()) graphic.raycastTarget = false;
            characterNameText.supportRichText = characterResponseText.supportRichText = characterStatusText.supportRichText = false;
            characterResponsePanel.gameObject.SetActive(false);

            conversationPanel = UIK.Box(hud, "OfficerTextInput", UIK.PanelSolid).rectTransform;
            conversationPanel.anchorMin = conversationPanel.anchorMax = new Vector2(0.5f, 0f);
            conversationPanel.pivot = new Vector2(0.5f, 0f);
            conversationPanel.anchoredPosition = new Vector2(0f, 32f);
            conversationPanel.sizeDelta = new Vector2(1000f, 0f);
            conversationPanel.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            UIK.VStack(conversationPanel.gameObject, 26, 20, 12);
            conversationTitle = UIK.Label(conversationPanel, "", 24, UIK.Accent, FontStyle.Bold);
            conversationTitle.supportRichText = false;
            UIK.Label(conversationPanel, "Enter sends from the text field; Tab changes focus; Esc closes. Movement resumes when this closes.", 21, UIK.Muted);
            UIK.Label(conversationPanel, "Offline demo: authored replies use distance, scene sound and waiting time; text is not interpreted.", 18, UIK.Muted);

            var field = UIK.Box(conversationPanel, "OfficerMessage", UIK.ButtonBg).rectTransform;
            UIK.Size(field.gameObject, minH: 64f, prefH: 64f);
            conversationInput = field.gameObject.AddComponent<InputField>();
            conversationInput.targetGraphic = field.GetComponent<Image>();
            conversationInput.characterLimit = 512;
            conversationInput.lineType = InputField.LineType.SingleLine;
            conversationInput.contentType = InputField.ContentType.Standard;
            conversationInput.customCaretColor = true;
            conversationInput.caretColor = UIK.Accent;
            conversationInput.selectionColor = new Color(0.18f, 0.77f, 0.71f, 0.35f);
            var fieldColors = conversationInput.colors;
            fieldColors.selectedColor = new Color(0.20f, 0.38f, 0.46f);
            conversationInput.colors = fieldColors;
            var inputText = UIK.Label(field, "", 27, UIK.Text, FontStyle.Normal, TextAnchor.MiddleLeft);
            inputText.supportRichText = false;
            inputText.horizontalOverflow = HorizontalWrapMode.Overflow;
            inputText.verticalOverflow = VerticalWrapMode.Truncate;
            UIK.Stretch(inputText.rectTransform, 16f, 8f, 16f, 8f);
            conversationInput.textComponent = inputText;
            var placeholder = UIK.Label(field, "Your message to Sam", 27, UIK.Muted, FontStyle.Italic, TextAnchor.MiddleLeft);
            placeholder.supportRichText = false;
            UIK.Stretch(placeholder.rectTransform, 16f, 8f, 16f, 8f);
            conversationInput.placeholder = placeholder;
            conversationMicrophone = UIK.MakeButton(conversationPanel, "Record microphone", () => recordMicrophone?.Invoke(), 22, false, 48);
            voiceStatusText = UIK.Label(conversationPanel, "Voice not initialized; typing remains available.", 18, UIK.Muted);
            voiceStatusText.supportRichText = false;
            var voiceToggleRow = UIK.Rect("SamVoiceToggle", conversationPanel);
            UIK.Size(voiceToggleRow.gameObject, minH: 36, prefH: 36);
            samVoiceToggle = voiceToggleRow.gameObject.AddComponent<Toggle>();
            var check = UIK.Box(voiceToggleRow, "Check", UIK.Accent).rectTransform;
            check.anchorMin = check.anchorMax = new Vector2(0, 0.5f); check.pivot = new Vector2(0, 0.5f);
            check.sizeDelta = new Vector2(28, 28);
            samVoiceToggle.targetGraphic = check.GetComponent<Image>(); samVoiceToggle.graphic = check.GetComponent<Image>();
            samVoiceToggle.isOn = true;
            var voiceLabel = UIK.Label(voiceToggleRow, "Sam spoken replies (Windows offline)", 21, UIK.Text);
            UIK.Stretch(voiceLabel.rectTransform, 42, 0, 0, 0);
            conversationSend = UIK.MakeButton(conversationPanel, "Send message", SubmitConversationInput, 24, true, 54);
            conversationClose = UIK.MakeButton(conversationPanel, "Close without sending", CancelConversationInput, 22, false, 48);
            conversationInput.onValueChanged.AddListener(value => conversationSend.interactable = !string.IsNullOrWhiteSpace(value));
            conversationPanel.gameObject.SetActive(false);
        }

        public void ConfigureVoice(Action onRecord, Action<bool> onOutputEnabled, bool available)
        {
            recordMicrophone = onRecord;
            samVoiceToggle.onValueChanged.AddListener(value => onOutputEnabled(value));
            conversationMicrophone.interactable = available;
            samVoiceToggle.interactable = available;
        }

        public void SetVoiceState(string status, bool recording, bool busy, bool available)
        {
            if (voiceStatusText.text != status) voiceStatusText.text = status;
            conversationMicrophone.interactable = available && !busy;
            conversationMicrophone.GetComponentInChildren<Text>().text = recording ? "Stop and transcribe" : "Record microphone";
        }

        public void ApplyVoiceTranscript(string text)
        {
            if (!ConversationInputOpen) return;
            conversationInput.text = text;
            Focus(conversationInput); conversationInput.ActivateInputField();
        }

        public void ShowConversationInput(string characterName, Action<string> onSubmit, Action onCancel)
        {
            HideConversationInput();
            submitConversation = onSubmit; cancelConversation = onCancel;
            conversationTitle.text = "Speak to " + characterName;
            conversationInput.SetTextWithoutNotify("");
            conversationSend.interactable = false;
            conversationOpenedFrame = Time.frameCount;
            conversationPanel.gameObject.SetActive(true);
            conversationPanel.SetAsLastSibling();
            conversationInput.interactable = false;
            focusConversation = StartCoroutine(FocusConversationNextFrame());
        }

        IEnumerator FocusConversationNextFrame()
        {
            yield return null;
            focusConversation = null;
            if (!ConversationInputOpen) yield break;
            conversationInput.interactable = true;
            Focus(conversationInput);
            conversationInput.ActivateInputField();
        }

        public void HideConversationInput()
        {
            if (conversationPanel == null) return;
            if (ConversationInputOpen) conversationClosedFrame = Time.frameCount;
            if (focusConversation != null) { StopCoroutine(focusConversation); focusConversation = null; }
            if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null &&
                EventSystem.current.currentSelectedGameObject.transform.IsChildOf(conversationPanel))
                EventSystem.current.SetSelectedGameObject(null);
            conversationInput.DeactivateInputField();
            conversationInput.SetTextWithoutNotify("");
            conversationPanel.gameObject.SetActive(false);
            submitConversation = null; cancelConversation = null;
        }

        void SubmitConversationInput()
        {
            if (!ConversationInputOpen || conversationOpenedFrame == Time.frameCount || string.IsNullOrWhiteSpace(conversationInput.text)) return;
            string text = conversationInput.text.Trim();
            var callback = submitConversation;
            HideConversationInput();
            callback?.Invoke(text);
        }

        void CancelConversationInput()
        {
            if (!ConversationInputOpen) return;
            var callback = cancelConversation;
            HideConversationInput();
            callback?.Invoke();
        }

        void MoveConversationFocus(bool backwards)
        {
            if (EventSystem.current == null) return;
            var selected = EventSystem.current.currentSelectedGameObject;
            var controls = new Selectable[] { conversationInput, conversationMicrophone, samVoiceToggle, conversationSend, conversationClose }
                .Where(control => control.IsInteractable() && control.gameObject.activeInHierarchy).ToList();
            int index = controls.FindIndex(control => control.gameObject == selected);
            var next = controls[(index + (backwards ? controls.Count - 1 : 1)) % controls.Count];
            conversationInput.DeactivateInputField();
            Focus(next);
            if (next == conversationInput) conversationInput.ActivateInputField();
        }

        public void SetCharacterResponse(string characterName, string response, string status)
        {
            characterNameText.text = characterName ?? "";
            characterResponseText.text = response ?? "";
            characterStatusText.text = status ?? "";
            characterResponseAvailable = true;
            characterResponsePanel.gameObject.SetActive(true);
        }

        public void ClearCharacterResponse()
        {
            characterResponseAvailable = false;
            if (characterResponsePanel != null) characterResponsePanel.gameObject.SetActive(false);
        }

        public void PositionCharacterResponse(Camera camera, Vector3 worldPosition)
        {
            if (!characterResponseAvailable || camera == null) return;
            Vector3 screen = camera.WorldToScreenPoint(worldPosition);
            bool visible = screen.z > 0f && screen.x >= 0f && screen.x <= Screen.width && screen.y >= 0f && screen.y <= Screen.height;
            characterResponsePanel.gameObject.SetActive(visible);
            if (!visible) return;
            var canvasRect = (RectTransform)canvas.transform;
            Vector2 point;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, null, out point);
            float halfWidth = characterResponsePanel.rect.width * 0.5f;
            float maxY = canvasRect.rect.yMax - Mathf.Max(100f, characterResponsePanel.rect.height) - 24f;
            point.x = Mathf.Clamp(point.x, canvasRect.rect.xMin + halfWidth + 24f, canvasRect.rect.xMax - halfWidth - 24f);
            point.y = Mathf.Clamp(point.y + 24f, canvasRect.rect.yMin + 24f, maxY);
            characterResponsePanel.anchoredPosition = point;
        }

        public void SetPrompt(string text)
        {
            bool show = !string.IsNullOrEmpty(text);
            if (promptBox.activeSelf != show) promptBox.SetActive(show);
            if (show && promptText.text != text) promptText.text = text;
        }

        public void SetHint(string text)
        {
            hintBox.SetActive(!string.IsNullOrEmpty(text));
            hintText.text = text ?? "";
        }

        public void RefreshHud(ScenarioSession s)
        {
            if (s == null) return;
            var lines = new List<string>();
            foreach (var o in s.Scenario.objectives)
            {
                bool done = o.flags.Count > 0 && o.flags.All(f => s.HasFlag(f));
                lines.Add(done ? UIK.Tint("[x]  " + o.text, UIK.Good) : "[  ]  " + o.text);
            }
            objectivesText.text = string.Join("\n", lines.ToArray());

            indicatorsBox.SetActive(GameSettings.ShowIndicators);
            string noise = s.Sensory >= 65 ? UIK.Tint("High", UIK.Bad) : (s.Sensory >= 35 ? UIK.Tint("Moderate", UIK.Warn) : UIK.Tint("Low", UIK.Good));
            string sam = s.Distress >= 65 ? UIK.Tint("Overwhelmed", UIK.Bad) : (s.Distress >= 40 ? UIK.Tint("Tense", UIK.Warn) : UIK.Tint("Calmer", UIK.Good));
            indicatorsText.text = "Noise and sensory load:  " + noise + "\nSam appears:  " + sam;
        }

        public void Toast(DecisionRecord rec)
        {
            toastText.text = UIK.Tint("<b>" + TagFor(rec) + "</b>", UIK.Accent) + "\n" + rec.Feedback;
            toastBox.SetActive(true);
            toastUntil = Time.unscaledTime + 10f;
        }

        // ------------------------------------------------------------------
        // Dialogue, feedback, waiting, outcome
        // ------------------------------------------------------------------

        void BuildDialogue()
        {
            var s = NewScreen("dialogue");
            dialoguePanel = UIK.Box(s, "Panel", UIK.Panel).rectTransform;
            dialoguePanel.anchorMin = dialoguePanel.anchorMax = new Vector2(0.5f, 0f);
            dialoguePanel.pivot = new Vector2(0.5f, 0f);
            dialoguePanel.anchoredPosition = new Vector2(0f, 36f);
            dialoguePanel.sizeDelta = new Vector2(1400f, 0f);
            dialoguePanel.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            UIK.VStack(dialoguePanel.gameObject, 34, 28, 14);
        }

        void ClearPanel()
        {
            if (waitRoutine != null) { StopCoroutine(waitRoutine); waitRoutine = null; }
            Clear(dialoguePanel);
            numberActions.Clear();
            choicesLocked = false;
        }

        void Pick(int i)
        {
            if (choicesLocked || i >= numberActions.Count) return;
            choicesLocked = true;
            numberActions[i]();
        }

        public void ShowDialogue(string speaker, string body, List<Choice> choices)
        {
            ClearPanel();
            DialogueActive = true;
            UIK.Label(dialoguePanel, (speaker ?? "").ToUpperInvariant(), 22, UIK.Accent, FontStyle.Bold);
            UIK.Label(dialoguePanel, body, 30, UIK.Text);
            Button first = null;
            for (int i = 0; i < choices.Count; i++)
            {
                int index = i; var ch = choices[i];
                numberActions.Add(ch.OnSelect);
                var b = UIK.MakeButton(dialoguePanel, UIK.Tint((i + 1).ToString(), UIK.Accent) + "    " + ch.Label, () => Pick(index), 27);
                if (first == null) first = b;
            }
            Only("hud", "dialogue");
            Focus(first);
        }

        public void HideDialogue()
        {
            ClearPanel();
            DialogueActive = false;
        }

        public void ShowFeedback(DecisionRecord rec, string response, Action onContinue)
        {
            ClearPanel();
            DialogueActive = true;
            UIK.Label(dialoguePanel, "EDUCATIONAL FEEDBACK    " + TagFor(rec), 21, UIK.Accent, FontStyle.Bold);
            if (!string.IsNullOrEmpty(response)) UIK.Label(dialoguePanel, "<i>" + response + "</i>", 29, UIK.Text);
            if (!string.IsNullOrEmpty(rec.Feedback)) UIK.Label(dialoguePanel, rec.Feedback, 28, UIK.Text);
            numberActions.Add(onContinue);
            var b = UIK.MakeButton(dialoguePanel, "Continue   (Enter)", () => Pick(0), 28, true);
            Only("hud", "dialogue");
            Focus(b);
        }

        public void ShowOutcome(ScenarioNode node, Action onContinue)
        {
            ClearPanel();
            DialogueActive = true;
            var tone = ScenarioParse.ToTone(node.outcomeTone);
            Color c = tone == OutcomeTone.Positive ? UIK.Good : (tone == OutcomeTone.Difficult ? UIK.Bad : UIK.Warn);
            UIK.Label(dialoguePanel, "OUTCOME", 22, UIK.Accent, FontStyle.Bold);
            UIK.Label(dialoguePanel, UIK.Tint(node.outcomeTitle, c), 42, UIK.Text, FontStyle.Bold);
            UIK.Label(dialoguePanel, node.outcomeSummary, 29, UIK.Text);
            numberActions.Add(onContinue);
            var b = UIK.MakeButton(dialoguePanel, "Continue to Debrief   (Enter)", () => Pick(0), 28, true);
            Only("hud", "dialogue");
            Focus(b);
        }

        /// <summary>Shows the processing-time wait (10-20 s by default) and calls done when it ends.</summary>
        public void ShowWait(string who, float seconds, Action done)
        {
            ClearPanel();
            DialogueActive = true;
            UIK.Label(dialoguePanel, "ALLOWING PROCESSING TIME", 22, UIK.Accent, FontStyle.Bold);
            UIK.Label(dialoguePanel, "You have spoken. Stay quiet and give " + (string.IsNullOrEmpty(who) ? "them" : who) + " time to process. Do not repeat yourself yet.", 30, UIK.Text);
            var bar = UIK.Box(dialoguePanel, "Bar", new Color(0.12f, 0.18f, 0.27f, 1f)).rectTransform;
            UIK.Size(bar.gameObject, minH: 16, prefH: 16);
            var fill = UIK.Box(bar, "Fill", UIK.Accent).rectTransform;
            fill.anchorMin = Vector2.zero; fill.anchorMax = new Vector2(0f, 1f); fill.offsetMin = fill.offsetMax = Vector2.zero;
            var counter = UIK.Label(dialoguePanel, "", 24, UIK.Muted);
            Only("hud", "dialogue");
            waitRoutine = StartCoroutine(WaitRoutine(seconds, fill, counter, done));
        }

        IEnumerator WaitRoutine(float seconds, RectTransform fill, Text counter, Action done)
        {
            float t = 0f;
            while (t < seconds)
            {
                t += Time.deltaTime;   // scaled time: pausing the game pauses the wait
                fill.anchorMax = new Vector2(Mathf.Clamp01(t / seconds), 1f);
                counter.text = Mathf.CeilToInt(Mathf.Max(0f, seconds - t)) + " s";
                yield return null;
            }
            waitRoutine = null;
            if (done != null) done();
        }

        // ------------------------------------------------------------------
        // Pause / settings
        // ------------------------------------------------------------------

        void BuildPause()
        {
            var s = NewScreen("pause");
            var p = CentrePanel(s, 760f, true);
            UIK.Label(p, "PAUSED", 24, UIK.Accent, FontStyle.Bold);
            UIK.Label(p, "SimSafe", 52, UIK.Text, FontStyle.Bold);
            UIK.MakeButton(p, "Resume", () => app.Resume(), 30, true);
            UIK.MakeButton(p, "Restart Scenario", () => app.BeginScenario(), 30);
            UIK.MakeButton(p, "Settings", () => app.OpenSettings(() => app.ReturnToPause()), 30);
            UIK.MakeButton(p, "Main Menu", () => app.GoToMenu(), 30);
        }

        public void ShowPause()
        {
            Only("pause");
            Focus(screens["pause"].GetComponentInChildren<Button>());
        }

        void BuildSettings() { var s = NewScreen("settings"); settingsBody = CentrePanel(s, 1000f, true); }

        public void ShowSettings(Action back)
        {
            settingsBack = back;
            Clear(settingsBody);
            UIK.Label(settingsBody, "SETTINGS", 24, UIK.Accent, FontStyle.Bold);
            Stepper("Master volume", () => Mathf.RoundToInt(GameSettings.MasterVolume * 100f) + "%", d => GameSettings.MasterVolume += d * 0.1f);
            Stepper("Mouse sensitivity", () => GameSettings.MouseSensitivity.ToString("0.0") + "x", d => GameSettings.MouseSensitivity += d * 0.1f);
            Stepper("Interface text size", () => Mathf.RoundToInt(GameSettings.UiScale * 100f) + "%", d => { GameSettings.UiScale += d * 0.1f; ApplyScale(); });
            Toggle("Show scene indicators (training aid)", () => GameSettings.ShowIndicators, v => GameSettings.ShowIndicators = v);
            Toggle("Shorten wait timers (demo mode)", () => GameSettings.ShortWaits, v => GameSettings.ShortWaits = v);
            UIK.Label(settingsBody, "Wait timers are 10-20 seconds by default, matching the guidance on allowing processing time.", 20, UIK.Muted);
            var b = UIK.MakeButton(settingsBody, "Back", () => { if (settingsBack != null) settingsBack(); }, 30, true);
            Only("settings");
            Focus(b);
        }

        void Stepper(string label, Func<string> value, Action<int> change)
        {
            var row = UIK.Rect("Row", settingsBody);
            UIK.HStack(row.gameObject, 14);
            UIK.Size(row.gameObject, minH: 60);
            var l = UIK.Label(row, label, 28, UIK.Text, FontStyle.Normal, TextAnchor.MiddleLeft);
            UIK.Size(l.gameObject, flexW: 1);
            Text v = null;
            var minus = UIK.MakeButton(row, "-", () => { change(-1); v.text = value(); RefreshHud(app.CurrentSession); }, 32);
            var plus = UIK.MakeButton(row, "+", () => { change(1); v.text = value(); RefreshHud(app.CurrentSession); }, 32);
            minus.GetComponentInChildren<Text>().alignment = TextAnchor.MiddleCenter;
            plus.GetComponentInChildren<Text>().alignment = TextAnchor.MiddleCenter;
            UIK.Size(minus.gameObject, minW: 70); UIK.Size(plus.gameObject, minW: 70);
            v = UIK.Label(row, value(), 28, UIK.Accent, FontStyle.Bold, TextAnchor.MiddleCenter);
            UIK.Size(v.gameObject, minW: 120);
            v.transform.SetSiblingIndex(2);   // minus, value, plus
        }

        void Toggle(string label, Func<bool> get, Action<bool> set)
        {
            Button b = null;
            Func<string> text = () => label + ":   " + (get() ? UIK.Tint("On", UIK.Good) : UIK.Tint("Off", UIK.Muted));
            b = UIK.MakeButton(settingsBody, text(), () => { set(!get()); b.GetComponentInChildren<Text>().text = text(); RefreshHud(app.CurrentSession); }, 28);
        }

        // ------------------------------------------------------------------
        // Debrief
        // ------------------------------------------------------------------

        void BuildDebrief()
        {
            var s = NewScreen("debrief");
            var bg = UIK.Box(s, "Bg", new Color(0.03f, 0.06f, 0.10f, 0.96f));
            UIK.Stretch(bg.rectTransform);

            var scroll = UIK.Rect("Scroll", s);
            UIK.Stretch(scroll, 160, 150, 160, 30);
            debriefScroll = scroll.gameObject.AddComponent<ScrollRect>();
            var vp = UIK.Rect("Viewport", scroll);
            UIK.Stretch(vp);
            vp.gameObject.AddComponent<RectMask2D>();
            vp.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0.001f);
            debriefContent = UIK.Rect("Content", vp);
            debriefContent.anchorMin = new Vector2(0f, 1f); debriefContent.anchorMax = new Vector2(1f, 1f);
            debriefContent.pivot = new Vector2(0.5f, 1f); debriefContent.offsetMin = debriefContent.offsetMax = Vector2.zero;
            UIK.VStack(debriefContent.gameObject, 8, 8, 18);
            debriefContent.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            debriefScroll.content = debriefContent; debriefScroll.viewport = vp;
            debriefScroll.horizontal = false; debriefScroll.vertical = true;
            debriefScroll.movementType = ScrollRect.MovementType.Clamped; debriefScroll.scrollSensitivity = 45f;

            var footer = UIK.Rect("Footer", s);
            footer.anchorMin = new Vector2(0f, 0f); footer.anchorMax = new Vector2(1f, 0f); footer.pivot = new Vector2(0.5f, 0f);
            footer.anchoredPosition = new Vector2(0f, 30f); footer.sizeDelta = new Vector2(-320f, 100f);
            var h = UIK.HStack(footer.gameObject, 20);
            h.childForceExpandWidth = true; h.childForceExpandHeight = true;
            UIK.MakeButton(footer, "Replay Scenario", () => app.BeginScenario(), 30, true);
            UIK.MakeButton(footer, "Scenario Selection", () => app.OpenScenarioSelect(), 30);
            UIK.MakeButton(footer, "Main Menu", () => app.GoToMenu(), 30);
        }

        static Color PrincipleColor(Principle p)
        {
            switch (p)
            {
                case Principle.Slow: return new Color(0.30f, 0.80f, 0.74f);
                case Principle.Lower: return new Color(0.50f, 0.70f, 0.98f);
                case Principle.Observe: return new Color(0.96f, 0.76f, 0.35f);
                case Principle.Wait: return new Color(0.76f, 0.66f, 0.98f);
                default: return UIK.Muted;
            }
        }

        static Color AlignmentColor(Alignment a)
        {
            switch (a)
            {
                case Alignment.Aligned: return UIK.Good;
                case Alignment.Mixed: return UIK.Warn;
                case Alignment.Misaligned: return UIK.Bad;
                default: return UIK.Muted;
            }
        }

        static string TagFor(DecisionRecord r)
        {
            return UIK.Tint(ScenarioParse.Label(r.Principle).ToUpperInvariant(), PrincipleColor(r.Principle))
                + "  -  " + UIK.Tint(ScenarioParse.Label(r.Alignment), AlignmentColor(r.Alignment));
        }

        static string StatusText(CriterionStatus s)
        {
            switch (s)
            {
                case CriterionStatus.Demonstrated: return UIK.Tint("Demonstrated", UIK.Good);
                case CriterionStatus.Partial: return UIK.Tint("Partly demonstrated", UIK.Warn);
                case CriterionStatus.Missed: return UIK.Tint("Missed opportunity", UIK.Bad);
                default: return UIK.Tint("Not encountered", UIK.Muted);
            }
        }

        void H(string text) { Spacer(debriefContent, 10); UIK.Label(debriefContent, text, 34, UIK.Text, FontStyle.Bold); }
        Text P(string text, int size = 26, Color? color = null) { return UIK.Label(debriefContent, text, size, color ?? UIK.Text); }

        public void ShowDebrief(Debrief d)
        {
            Clear(debriefContent);
            P("TRAINING DEBRIEF", 24, UIK.Accent).fontStyle = FontStyle.Bold;
            P(d.ScenarioTitle, 52).fontStyle = FontStyle.Bold;
            P(d.Status, 22, UIK.Warn);

            Color tone = d.Tone == OutcomeTone.Positive ? UIK.Good : (d.Tone == OutcomeTone.Difficult ? UIK.Bad : UIK.Warn);
            H("Outcome");
            P(UIK.Tint("<b>" + d.OutcomeTitle + "</b>", tone), 34);
            if (!string.IsNullOrEmpty(d.OutcomeSummary)) P(d.OutcomeSummary);
            if (!string.IsNullOrEmpty(d.NonGuaranteeNote)) P("<i>" + d.NonGuaranteeNote + "</i>", 23, UIK.Muted);
            foreach (var o in d.Observations) P(o, 24, UIK.Warn);

            H("SLOW principles in this playthrough");
            foreach (var ps in d.Principles)
                P(UIK.Tint("<b>" + ScenarioParse.Letter(ps.Principle) + "   " + ScenarioParse.Label(ps.Principle) + "</b>", PrincipleColor(ps.Principle)) + "   -   " + ps.Headline);

            H("Learning criteria");
            foreach (var c in d.Criteria)
                P("<b>" + c.Criterion.title + "</b>   " + StatusText(c.Status) + "\n" + UIK.Tint(c.Criterion.description, UIK.Muted), 25);

            H("Decisions you made");
            if (d.Decisions.Count == 0) P("No decisions were made.", 25, UIK.Muted);
            foreach (var r in d.Decisions)
            {
                string head = "<b>" + r.Order + ".</b>  " + (r.IsEnvironmentInteraction ? "Environment action  -  " : "") + TagFor(r);
                var card = UIK.Box(debriefContent, "Decision", new Color(0.07f, 0.12f, 0.20f, 1f));
                UIK.VStack(card.gameObject, 26, 20, 8);
                UIK.Label(card.transform, head, 24, UIK.Text);
                UIK.Label(card.transform, "You chose: " + r.ChosenText, 27, UIK.Text);
                if (!string.IsNullOrEmpty(r.Feedback)) UIK.Label(card.transform, UIK.Tint("Why it matters: ", UIK.Accent) + r.Feedback, 25, UIK.Text);
                if (r.WaitSeconds > 0.01f) UIK.Label(card.transform, "Processing time allowed: " + Mathf.RoundToInt(r.WaitSeconds) + " s", 22, UIK.Muted);
                if (r.Alternatives.Count > 0)
                {
                    var alt = r.Alternatives.Select(a => "-  " + a.Text + "  (" + UIK.Tint(ScenarioParse.Label(a.Alignment), AlignmentColor(a.Alignment)) + ")"
                        + (string.IsNullOrEmpty(a.Note) ? "" : "\n    " + UIK.Tint(a.Note, UIK.Muted))).ToArray();
                    UIK.Label(card.transform, UIK.Tint("Other options at this point", UIK.Accent) + "\n" + string.Join("\n", alt), 23, UIK.Text);
                }
            }

            H("Suggestions for improvement");
            if (d.Suggestions.Count == 0) P("None were triggered by this playthrough.", 25, UIK.Muted);
            foreach (var s in d.Suggestions) P("-  " + s, 26);

            Spacer(debriefContent, 8);
            var box = UIK.Box(debriefContent, "Disclaimer", new Color(0.25f, 0.18f, 0.05f, 1f));
            UIK.VStack(box.gameObject, 26, 20, 0);
            UIK.Label(box.transform, UIK.Tint("<b>Illustrative feedback only.</b>  ", UIK.Warn) + d.Disclaimer, 23, UIK.Text);
            Spacer(debriefContent, 30);

            debriefScroll.verticalNormalizedPosition = 1f;
            Only("debrief");
            Focus(screens["debrief"].GetComponentInChildren<Button>());
        }

        // ------------------------------------------------------------------

        void Update()
        {
            if (toastBox != null && toastBox.activeSelf && Time.unscaledTime > toastUntil) toastBox.SetActive(false);

            if (ConversationInputOpen)
            {
                if (conversationOpenedFrame != Time.frameCount)
                {
                    if (Input.GetKeyDown(KeyCode.Escape)) CancelConversationInput();
                    else if (Input.GetKeyDown(KeyCode.Tab))
                        MoveConversationFocus(Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift));
                    else if ((Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) &&
                        string.IsNullOrEmpty(Input.compositionString) && EventSystem.current != null &&
                        EventSystem.current.currentSelectedGameObject == conversationInput.gameObject)
                        SubmitConversationInput();
                }
                return;
            }
            if (ConversationInputConsumedThisFrame) return;

            if (numberActions.Count > 0)
                for (int i = 0; i < numberActions.Count && i < 9; i++)
                    if (Input.GetKeyDown(KeyCode.Alpha1 + i) || Input.GetKeyDown(KeyCode.Keypad1 + i)) { Pick(i); break; }

            if (debriefScroll != null && debriefScroll.gameObject.activeInHierarchy)
            {
                if (Input.GetKeyDown(KeyCode.PageDown)) debriefScroll.verticalNormalizedPosition = Mathf.Clamp01(debriefScroll.verticalNormalizedPosition - 0.25f);
                if (Input.GetKeyDown(KeyCode.PageUp)) debriefScroll.verticalNormalizedPosition = Mathf.Clamp01(debriefScroll.verticalNormalizedPosition + 0.25f);
            }
        }
    }
}
