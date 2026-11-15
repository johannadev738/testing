# SimSafe v0.2 event and offline character foundation

Implemented 2026-10-21 after local baseline commit `49e8e83`.
No packages, paid services or visual replacements were installed.

## Implemented code

- `SimulationEvent.cs`: GUID session/event IDs; bounded metadata identifiers;
  finite nonnegative elapsed seconds; validated event types; sealed immutable
  start/end/control payloads. The closed payload hierarchy prevents arbitrary
  provider objects or mutable collections from entering event snapshots.
- `SimulationEventRecorder.cs`: injected clock, strict session boundaries,
  nondecreasing time, process Stopwatch clock and defensive read-only snapshots.
  Failed validation does not append events or replace completed session history.
- `ScenarioEventMonitor.cs`: pure-C# observer of existing successful results.
  It accepts only an actual current-session car_siren decision with sirenOff set;
  records it once and ignores null, forged, foreign or unrelated decisions.
- `SimSafeApp.cs`: starts monitoring only after ScenarioSession.Start succeeds;
  ends on outcome, menu return, restart or destruction/quit. Successful patrol
  siren interaction is observed after the existing scenario action. Read a safe
  snapshot through `SimSafeApp.MonitoringEvents`.
- `CharacterInteraction.cs`: immutable individual profiles and explicit UAI/KAI
  context; SPEAK, WAIT, REQUEST_SPACE, GESTURE, MOVE_AWAY intentions; cancellable
  STT/dialogue/TTS/lip-sync contracts; bounded, copied PCM transport.

ScenarioSession, Debrief, PlayerController, UiManager, WorldBuilder, Humanoid,
scenario JSON, scenes, materials, package pins and project settings are preserved.
New C# source/test files have Unity .meta files.

## Recording semantics

Runtime time is real elapsed process time including pauses, not wall-clock time.
The first event is at zero. Equal timestamps are valid and retain insertion order.
Clock regressions fail validation rather than silently inventing a timestamp.
Snapshots cannot be modified and do not change when another event/run is recorded.
Only the latest run is retained by the recorder; consumers must hold an earlier
snapshot if needed. No automatic persistence, telemetry upload or file export exists.

The initial control payload reports the logical scenario siren state, not a physical
sound-level measurement. Switching the siren off also controls lights in the existing
WorldRig; that coupling is preserved. Arrival dialogue choices that already disable
the siren are not misreported as the successful E-key environmental interaction.

The recorder is for the simulation thread. Do not call it directly from background
provider callbacks. This is evidence plumbing; no new training scores are derived.

## Offline text demonstration

This is callable/tested pure C# preparation, not a new Unity dialogue screen:

```csharp
var profile = new CharacterProfile("demo-sam", 12, 2,
    "I can listen now.", "Please lower that noise.", "Please give me space.");
var turn = new CharacterTurn(sessionId, Guid.NewGuid(), ScenarioCategory.KAI,
    "Hello, how can I help?", 3, 12, false);
var intention = new OfflineCharacterBehavior().Evaluate(profile, turn);
string text = await new OfflineDialogueProvider().GenerateAsync(
    turn, intention, cancellationToken);
```

This fictional, provisional profile asks for space inside its authored threshold,
waits for its processing interval, then selects an authored quiet/noisy line.
Other profiles can use different intervals, distances and responses. Officer text
is ephemeral input and is not interpreted, diagnosed or automatically recorded.
The offline provider rejects intentions from other sessions/turns and honors
cancellation. Runtime callers must also discard results once a turn becomes stale.
Gesture/move-away are contracts only; no scene action executor is included.

The current scenario discloses autism in dispatch, consistent with KAI. Its JSON
is unchanged; no new scenario category is guessed or silently migrated into it.
The separate contracts support explicit UAI as well. Both need reviewed content.

## Reproduce tests

From repository root:

```powershell
dotnet restore SimSafe/tools/harness/Harness.csproj
dotnet build SimSafe/tools/harness/Harness.csproj --no-restore -warnaserror
dotnet run --project SimSafe/tools/harness/Harness.csproj --no-build --no-restore
```

The harness includes every EditMode `*Tests.cs` file and the real Core sources,
using its existing NUnit shim. UnityScenarioSource is intentionally excluded from
the harness; the equivalent JSON loader remains in use outside Unity.

Tests cover payload/ID/type validation, invalid clocks, equal/regressing time,
session endings and overlap, immutable isolated snapshots, successful/blocked/
repeated/forged/foreign interactions, completed scenarios, individual profiles,
explicit categories, offline cancellation/correlation and defensive PCM buffers.
The original scenario suite still covers branching, state effects and debrief.

Final validation on 2026-10-21:

| Check | Actual result |
|---|---|
| Standalone build with `-warnaserror` | Exit 0; 0 warnings, 0 errors |
| Complete harness | 67 passed, 0 failed; exit 0 |
| Suite breakdown | 28 original scenario + 20 event + 7 monitor + 12 character tests |
| Unity Core compilation | Exit 0; 8 source files; no diagnostics |
| Unity Runtime compilation | Exit 0; 10 source files; 6 existing CS0618 warnings |
| Unity EditMode test assembly compilation | Exit 0; 5 source files; no diagnostics |
| Unity Test Runner / Play Mode | Not executed for this revision |

The independent compile completed at 1:11:42 PM America/New_York using the installed
editor's bundled .NET SDK 8.0.318/Roslyn, netstandard2.1 framework/package references
and cached editor defines. Runtime/tests reference the newly compiled Core output.
SHA-256 source digests before/after confirmed no source changed during this run.
All outputs went to ignored Logs/Verification; live Library outputs were untouched.
Runtime warnings are existing FindFirstObjectByType, FindObjectsByType and
FindObjectsSortMode usages; they were not expanded into unrelated API changes.

TDD first confirmed missing event/monitor/character types failed compilation before
their implementations were added. Subsequent tests passed; review replaced the
Unity teardown clock with Stopwatch and the final full suite passed again.

## Unity validation and remaining checks

The batch startup blocker is an already-open interactive Unity 6000.6.6f1 editor
using this exact project. An elevated read-only process inventory and Windows
Restart Manager resource-owner query identify that editor as the owner of
Temp/UnityLockfile. Sandboxed process listing failed to show it. No process was
terminated and no lock or cache was deleted. The prior batch log alone was too
terse to establish the cause; the independent lock-owner evidence resolves the
current blocker. Do not attribute it to source or license failure.

Unity documents that the same project cannot run interactively and in batch mode
simultaneously:
https://docs.unity.com/en-us/engine/6000.6/manual/unity-editor/command-line-arguments/editor

Independent Unity compiler verification uses the installed editor's Roslyn and
cached response-file references, with all outputs redirected to ignored Logs.
This checks compilation only. It is not Unity Test Runner execution, a player
build or Play Mode validation. See the final validation record below.

In the already-open editor, run Test Runner > EditMode > Run All. Alternatively,
save/close that editor normally before running batch tests. Then verify:

1. Main menu by mouse and keyboard, focus/submit, briefing and scenario launch.
2. Officer movement, mouse capture, pause/resume and E interactions.
3. Monitor snapshot starts at zero; successful car_siren adds one event, blocked
   or repeated siren interaction adds none; radio/mower do not add this event.
4. Outcome ends the run once; menu/quit/restart uses the appropriate boundary;
   replay has a new session ID and no events from the previous run.
5. Existing dialogue, waits, all outcomes and debrief remain unchanged.
6. Windows/WebGL builds, browser timing/audio/input and runtime errors.

## Next smallest working character milestone

Add one explicitly opted-in offline character presenter: sample proximity and
logical sensory state, accept text input, construct a correlated CharacterTurn,
show WAIT/REQUEST_SPACE/SPEAK plus its reason, cancel stale turns, and leave the
existing scenario/debrief path intact. Test the measured processing timer and
event-to-intention mapping before adding speech or autonomous movement.

Voice capture/recognition/synthesis, facial animation packages, licensed humanoids,
event-based SLOW scoring and the prefab scene conversion remain future work.
