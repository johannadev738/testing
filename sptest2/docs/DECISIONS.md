# SimSafe architecture decisions

## 2026-10-21: Observe existing scenarios without replacing their rules

Keep ScenarioSession, Debrief and scenario JSON unchanged. SimulationEventRecorder
owns validated, immutable telemetry only. ScenarioEventMonitor is a small pure-C#
adapter that observes successful scenario results and completion; it never chooses
options, applies effects or changes SLOW evaluation. This keeps the recorder
general and allows integration behavior to be tested without UnityEngine.

Record only session boundaries and an actual successful car_siren interaction in
this slice. Do not label arrival choices, model intentions or animations as observed
environmental actions. Later event kinds require their own typed payloads and tests.

## 2026-10-21: Use an injected process clock and immutable snapshots

Runtime uses StopwatchSimulationClock, independent of Unity frame/lifecycle state.
Elapsed times include pauses and are not UTC, training wait durations or active-play
time. The recorder rejects nonfinite, negative and regressing clock readings; equal
readings are allowed and snapshot order defines their order. Do not subtract pause
time or derive SLOW scores from these timestamps without a reviewed timing policy.

Avoid Time.realtimeSinceStartupAsDouble during teardown: Unity explicitly documents
incorrect values in OnDestroy. Fake clocks support deterministic tests. A new run
gets a new GUID and clears the recorder's current history; previously acquired
read-only snapshots remain independent. There is no disk export or automatic
audio/transcript storage. The recorder is single-threaded; future provider callbacks
must be marshalled to the simulation thread.

References:
- https://docs.unity3d.com/6000.6/Documentation/ScriptReference/Time-realtimeSinceStartupAsDouble.html
- https://learn.microsoft.com/dotnet/api/system.diagnostics.stopwatch

## 2026-10-21: Character providers cannot become training authorities

CharacterProfile and CharacterTurn are separate from existing scenario data.
UAI/KAI is explicitly authored context, never inferred from speech or appearance.
OfflineCharacterBehavior is a provisional stateless demonstration using individual
profile thresholds and authored lines. It is not a universal autism model.

Response intentions are bounded presentation suggestions, not executed actions.
Session/turn correlation and cancellation are mandatory when wiring asynchronous
providers. Keep STT, dialogue, TTS and lip-sync interfaces vendor neutral and free of
UnityEngine types. Existing debrief remains the only current evaluation path;
event-supported SLOW evidence and content still require NASC review.

Do not import speech/model packages or change the scene as part of this foundation.
See VISUAL_INTEGRATION_PLAN.md for the separate proposed asset workstream.

## 2026-10-21: Add offline free-roam text turns without replacing scenario flow

Use a pure-C# OfflineCharacterConversation with the same injected clock/recorder
and existing individual-profile policy. Record discrete threshold/sensory changes
and exact conditions for each response, not per-frame telemetry. Retain only typed
turn metadata; no raw transcript, semantic interpretation or diagnosis is added.

Processing time excludes pauses while event time includes them. A new submission
interrupts/restarts processing; range exit, lifecycle changes and scripted dialogue
cancel pending work. Captions are nonmodal and only typing temporarily owns input.
Keep F as the existing scenario-choice route on Sam, and preserve Dana/environment
interactions and debrief. Produced intentions are not proof of played speech or
completed animation. See OFFLINE_IN_SCENE.md for actual verification limits.
