# Offline in-scene character interaction

Implemented on `feat/simsafe-v0.2`, building on `49e8e83`, `0aca065`, `47cc624`.
No packages, paid services, models or scene replacements were installed.

## Integrated behavior

The existing SimSafeApp owns a shared clock/recorder for scenario monitoring and
an OfflineCharacterConversation. Only the residential welfare-call scenario opts
in. Its authored dispatch discloses autism, so its mapping is explicitly KAI;
there is no inference from typed content, appearance, distance or a response.

The new coordinator uses the existing CharacterProfile and OfflineCharacterBehavior.
Sam's fictional demo profile uses a 12-second processing interval and 2-metre
personal-space threshold; these provisional values are not NASC-approved or
universal. E interaction is limited to 4.5 metres centre-to-centre on the ground
plane, plus the existing player's interaction ray/collider check.

- E on Sam opens text entry in the existing Canvas/EventSystem. Officer input is
  temporarily suspended while typing. Send/Enter or Escape/Close restores movement.
- Submission starts a measured turn. WAIT shows remaining processing time and
  movement remains available. Moving inside 2 metres produces REQUEST_SPACE;
  stepping back can restore WAIT or allow SPEAK once the interval is satisfied.
- At a sufficient distance, the authored noisy/quiet reply depends on siren and
  radio flags at evaluation time. These represent logical scenario state, not a
  physical sound measurement. Mower/ambient sound is not part of this policy yet.
- A new submitted message interrupts the pending turn and restarts its interval.
  Leaving interaction range cancels the turn, and does not trigger a late reply
  when the officer returns. Completed captions also clear when leaving range.
- Pausing/focus loss freezes processing time. Recorder timestamps still measure
  real elapsed process time, including pauses; condition payloads separately
  describe active processing seconds.
- A head-following, click-through caption shows the response. SPEAK captions last
  six seconds; WAIT/REQUEST_SPACE track the pending state. A policy-produced event
  is not evidence that audio was played or an animation executed.
- F on Sam opens the unchanged scenario choices. Dana and environmental E
  interactions retain their existing paths. Pending text turns are cancelled
  before scripted dialogue, restart, menu return, completion or quit.

Text is transient, bounded to 512 characters, and never logged or persisted.
This milestone does not understand message meaning: typing supplies a turn and
its timing, while the existing policy uses measured conditions and authored lines.
The composer explicitly describes that limitation. No medical classification,
new SLOW scores or automatic scenario decisions are produced.

## Event evidence

Appended event types preserve all earlier enum values:

| Event | Retained metadata |
|---|---|
| OfficerProximityChanged | Distance, interaction-range and personal-space crossings |
| SceneSensoryChanged | Siren and radio logical states |
| OfficerTextSubmitted | Turn GUID and character count; no raw text |
| CharacterConditionsObserved | Turn, explicit UAI/KAI category, exact distance, sound flags, measured processing seconds |
| CharacterResponseProduced | Turn, intention, reason, remaining wait, immutable evidence-event GUIDs |
| CharacterTurnCancelled | Turn and interrupted/out-of-range/cancel reason |

Each response references its earlier submission and exact condition snapshot,
plus the latest proximity and sensory events from the same session. Stable
per-frame observations do not generate duplicate events. WAIT countdown refreshes
do not produce another response event. Records remain available through
`SimSafeApp.MonitoringEvents` as defensive immutable snapshots for future reviewed
SLOW evidence; the existing debrief is unchanged and does not yet consume them.

## Changed code

- Core/CharacterConversationEvents.cs: immutable validated payloads.
- Core/OfflineCharacterConversation.cs: deterministic scheduling and causal links.
- Core/SimulationEvent.cs: appended event kinds.
- Runtime/SimSafeApp.cs: scene sampling, Sam routing, caption/lifecycle management.
- Runtime/PlayerController.cs: brief frame suppression when returning from typing.
- Runtime/UiManager.cs: composer, caption, focus ownership and click-through HUD.
- Tests/EditMode/OfflineCharacterConversationTests.cs: 22 new tests, plus .meta files.

Scene assets, scenario JSON, ScenarioSession, Debrief, WorldBuilder, Humanoid,
package pins, project settings and all original tests were preserved.

## Verification actually performed

| Check | Actual result |
|---|---|
| Full standalone build, warnings as errors | Exit 0; 0 warnings, 0 errors |
| Complete harness | 89 passed, 0 failed, including all previous 67 |
| Unity 6000.6.6f1 Core compilation | 10 sources; exit 0; no warnings |
| Unity Runtime compilation | 10 sources; exit 0; 6 existing CS0618 warnings |
| Unity EditMode test assembly compilation | 6 sources; exit 0; no warnings |
| Unity Test Runner / actual Play Mode | Not executed for this revision |

The new tests first failed for missing coordinator types, then passed with the
implementation. They cover threshold equality/crossings, time-before/after the
profile interval, profile differences, sensory changes, WAIT/REQUEST_SPACE/SPEAK
transitions, interruption, pause boundaries, cancellation, stale sessions,
clock/input validation, causal references and defensive payload copies.

Independent assembly compilation used the installed editor's bundled Roslyn and
cached Unity/netstandard/package references. Runtime/tests reference the newly
compiled Core. Source digests stayed stable during the final compile. Outputs
remain ignored under Logs/Verification; live Library outputs were not rewritten.
This is compilation evidence, not engine execution or a Windows/WebGL build.

The open interactive editor still owns Temp/UnityLockfile, confirmed by Windows
Restart Manager. No editor was closed, no process killed and no lock removed.
Use that editor for the following tests, or save/close it normally before batch
testing. Do not open the same project in a competing batch instance.

## Required manual acceptance steps

1. In the existing Unity 6000.6.6f1 editor, wait for script compilation, inspect
   Console, open Assets/SimSafe/Scenes/Main.unity, and run EditMode tests. Expect
   the same 89 tests; report actual runner results rather than relying on compilation.
2. Press Play > Start Training > Begin. Choose "Switch off the siren and lights,
   then step out slowly", continue feedback, then "Pause by the car and observe"
   and continue. This reaches the original free-roam choose_path node.
3. Walk toward Sam with WASD, mouse-look and the interaction ray. Within 4.5
   metres centre distance, confirm E text/F scenario prompts. Stay at least 2
   metres away for the timing test. Check no prompt through an obstructing collider.
4. Press E. Confirm movement/look pause and cursor/text caret appear. Type letters
   including E, F, P, digits, punctuation and literal angle brackets. None should
   move the officer, pause the game, select scenario options or render rich markup.
   Empty/whitespace input must not send. Check the 512-character limit.
5. Send with Enter and with mouse Send on separate turns. Movement/look must
   resume without a leaked keystroke. Tab/Shift+Tab should cycle input, enabled
   Send and Close; Enter while Close is focused must close WITHOUT sending.
   Escape also closes without submitting; no duplicate send/cancel should occur.
6. Submit a valid message while 2-4.5 metres away. Confirm WAIT counts down for
   the profile's actual 12 seconds, unaffected by the old scripted demo-wait setting.
   Walk/look while waiting. At the threshold, expect Sam's authored noisy reply
   while the radio remains loud. There is no synthesized voice yet.
7. Return to the patrol vehicle and use E to lower radio volume (siren is already
   off on this route). Return and submit again. After waiting, expect the quiet
   reply. Replay using "Request additional units before getting out" then observe
   to test the siren still active as well; this is a test path, not training advice.
8. During a pending turn, move inside 2 metres: expect REQUEST_SPACE. Step back
   inside 4.5 metres: expect WAIT or SPEAK according to elapsed processing time.
   Submit another message before completion: its own full interval must restart.
9. Pause during WAIT with P after closing the composer. Wait longer than 12 real
   seconds, resume, and confirm remaining processing time was frozen. Alt-tab
   away should pause and discard any unsent draft; resume using the pause UI.
10. Leave 4.5 metres during WAIT. Returning must not produce a stale reply; start
    a new turn. Look away/back to check caption visibility; completed SPEAK text
    should expire after six seconds. Check lower resolutions/UI scale for overlap.
11. With a debugger attached, break in RefreshSamCaption and inspect the public
    MonitoringEvents snapshot. Locate OfficerTextSubmitted, its matching
    CharacterConditionsObserved and CharacterResponseProduced. Verify same
    session/turn, earlier evidence GUIDs and measured distance/sound/wait metadata.
    Confirm no raw typed text in event payloads. This is the current inspection
    path; no new telemetry/debrief screen was added.
12. Press F while looking at Sam to continue the original choices. Complete a
    scenario and inspect the unchanged debrief. Test Dana, patrol controls,
    pause/resume, menu return and replay. A new run must have a new session ID
    with no old-turn responses. Inspect Console for errors and EventSystem for
    a single active input path. Windows/WebGL build/input tests remain required.

## Next phase

See NEXT_CHARACTER_PHASE.md for licensed realistic humanoid integration, a narrow
presentation adapter, lip-sync calibration, prerecorded fallback and eventual
approved voice/STT/dialogue providers. The procedural scene remains in place.
