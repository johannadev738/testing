# Next character phase: asset, mouth animation and voice preparation

Status: proposed work after the offline in-scene milestone. No model, animation,
lip-sync package, voice model, microphone service or AI service is imported or
enabled by this plan. The procedural scene and offline text path remain the
fallback. Existing scenario/debrief rules remain authoritative.

## Smallest replacement slice

Replace Sam's presentation only, behind an explicit opt-in prefab binding. Keep
the same character ID, interaction collider, head-caption anchor and event source
so an approved visual replacement does not create a second character controller.
WorldRig currently stores concrete sealed procedural Humanoid references; a narrow
presentation adapter and parity tests are required before changing that binding.
Do not assume an imported Animator can be substituted without this adapter.

First validate one licensed realistic adult model in a separate preview scene:

- Metre scale, Unity Humanoid skeleton, stable root/feet, simple collision and
  documented eye/head/mouth anchor locations.
- URP 17.6 skin, eye and hair materials tested in Unity 6000.6.6f1; no conversion
  claim based solely on a marketplace compatibility badge.
- Independently controllable mouth/jaw shapes, closed-mouth rest, blink and gaze;
  a neutral baseline that composes with authored expressions.
- Retargeted idle/walk/turn/stop and a small reviewed speaking/request-space set.
- LODs and texture/shadow/transparent-hair budgets profiled on Windows and Web.
- Asset-specific redistribution and likeness rights, clip rights and third-party
  notices checked before import. Purchase or restricted downloads need approval.

See VISUAL_INTEGRATION_PLAN.md for residential environment specifications and
proposed performance budgets; those targets are not measured acceptance results.

## Mouth animation before generated voice

After package approval, evaluate only uLipSync's core with licensed prerecorded
dialogue first. Its character samples have separate licensing and should not be
imported by default. Pin the inspected revision and record package notices.

Implement the existing ILipSyncDriver behind the runtime presentation adapter.
One audio playback owner supplies both sound and mouth timing. Keep mouth weights
separate from blink/gaze/expression weights, reset them on cancellation/silence,
and ensure an old session cannot animate a restarted character. Prebaked dialogue
is a deterministic fallback and should use the same session/turn cancellation path.

Validate Windows first, then actual browser output separately: clip readability,
sample rate, audio-unlock gesture, long-utterance sync, pause/resume and silence.
Do not claim that uLipSync's supplied microphone helper works on WebGL.

## Voice and AI behind the current contracts

Add implementations of ISpeechToTextProvider, IDialogueProvider and
ITextToSpeechProvider only after choosing and approving the deployment approach.
Keep provider credentials on an approved backend, never in prefabs, Resources,
StreamingAssets or distributed builds. No provider code can directly change
ScenarioSession, score SLOW or execute unconstrained movement.

Start with push-to-talk and one voice. Preserve text entry and prerecorded output
when permission is denied, a provider times out, the network is unavailable or an
output fails validation. Correlate every request/result with session and turn,
cancel obsolete requests, bound response sizes and discard stale completions.

Generated wording must fit an approved response intention and individual profile.
UAI/KAI remains authored scenario context; speech, appearance or expressions must
not be used to infer a diagnosis. Intentions are not proof that audio, animation or
movement was delivered: record actual presentation start/end/cancellation separately
when those runtime adapters exist.

Review model/voice licenses, service content-use/retention terms and costs before
installing or enabling anything. This plan grants no new external-service access
and changes no privacy policy. Keep raw audio and officer transcripts transient by
default; store only the approved evidence metadata required for evaluation.

## Verification gate

1. Original scenario/debrief and offline interaction tests remain green.
2. Add adapter tests for cancellation, stale sessions, invalid clips and silence.
3. In Play Mode, compare procedural and prefab Sam IDs, interaction distance,
   movement/pose transitions, caption anchoring and event causality.
4. Check desktop and browser input/audio/visual behavior and measured performance.
5. Review character portrayal and educational feedback with NASC before training use.

Only after those gates should the prefab become the default or the residential
scene migrate from its current procedural implementation.
