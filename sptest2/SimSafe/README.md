# SimSafe (prototype vertical slice)

Interactive police-training simulation for the National Autism Safety Council (NASC), built around the
NASC **SLOW** framework (Slow Down, Lower Sensory, Observe Scene, Wait Patiently).

> **Status: illustrative prototype.** All scenario text, criteria and feedback are placeholder content.
> Nothing here has been reviewed or approved by NASC, and it does **not** measure real-world officer competence.
> No audio, model or texture assets are bundled: the world is generated from primitives and the sound is synthesised.

## What is verified and what is not

The offline in-scene milestone adds Sam text interaction, measured response timing
and linked evidence events. **89 tests pass** in the standalone harness; current
Core/Runtime/EditMode assemblies compile against Unity 6000.6.6f1 references.
Live Play Mode input and Unity Test Runner execution remain unverified. See
[controls, implementation and manual acceptance steps](../docs/OFFLINE_IN_SCENE.md)
and [the next character/voice phase](../docs/NEXT_CHARACTER_PHASE.md).

Current v0.2 foundation verification (2026-10-21): **67 passed, 0 failed** in the
standalone harness, including the original 28 scenario tests. Core, Runtime and
EditMode test assemblies compile against the installed Unity 6000.6.6f1 references.
Unity Test Runner execution and current Play Mode behavior remain unverified.
See [foundation/setup and validation notes](../docs/V02_FOUNDATION.md),
[architecture decisions](../docs/DECISIONS.md) and the
[proposed visual integration plan](../docs/VISUAL_INTEGRATION_PLAN.md).

The table below records the original prototype verification, before this foundation.

| Part | Status |
|---|---|
| Scenario data, branching, state, evaluation, debrief (`Scripts/Core`, `Resources/Scenarios`) | **Compiled and tested** with .NET 8: 28 tests pass (see below). |
| Unity layer (`Scripts/Runtime`, `Scripts/Editor`): world, characters, player, UI, audio | **Written but never opened in Unity.** Only a syntax-level check was possible. Expect to fix some compile errors or API mismatches on first open. |
| Visuals, animation feel, UI layout, audio levels | **Unseen.** Nobody has looked at the rendered result yet. |
| Windows and WebGL builds | **Not built.** Build entry points exist (`SimSafe > Build`) but are untested. |
| Unity Test Runner (EditMode) | **Not run.** The same test file passes under a shim harness outside Unity. |

## Launching it

Requires **Unity 6 (6000.x)**.

1. Create a new project from the **Universal 3D** template (this gives you URP).
2. Copy this repo's `Assets/SimSafe` folder into the new project's `Assets` folder.
3. Menu **SimSafe > 1. Setup Project**. It saves the base materials, creates `Scenes/Main.unity`, adds it to
   Build Settings, sets WebGL options and sets *Active Input Handling* to **Both**. Accept the editor restart if asked.
4. Open `Assets/SimSafe/Scenes/Main.unity` and press **Play**.
   (SimSafe builds everything at runtime, and switches off any template camera/lights, so the scene can stay empty.)

If step 3 is skipped, Play still works in the editor but materials fall back to `Shader.Find`, which is not reliable in builds.

**Builds:** `SimSafe > Build > Windows 64-bit` or `WebGL` (output in `Builds/`). Batch mode:
`Unity -batchmode -quit -projectPath . -executeMethod SimSafe.EditorTools.SimSafeSetup.BuildWebGL`.
For WebGL, serve the output folder over HTTP (for example `python3 -m http.server`); do not open `index.html` from disk.

## Controls

WASD move (walking pace only, there is no sprint), mouse look, **E** interact, **Esc / P** pause.
In free roam, **E on Sam** opens offline text input and **F on Sam** keeps the
original scenario choices. **Enter/Send** submits; **Escape/Close** discards;
**Tab/Shift+Tab** changes composer focus. Movement resumes after the composer
closes and stays available while Sam processes/responds.
Dialogue choices: click, Tab/arrows + Enter, or number keys. In the browser, click the game to capture the mouse.

## The scenario: Residential Welfare Call

1. **Dispatch briefing**, then arrive with siren, lights and radio still running.
2. Decide how to arrive, whether to observe, and whether to bring backup.
3. Free roam: switch off the siren / turn down the radio (patrol car) and ask the neighbour to pause the mower (walk over, E).
   Each is recorded as a decision.
4. Speak to Dana (caregiver) for information, or engage Sam directly.
5. Contact with Sam: short words, then a **10-20 s wait** (a real timer; "demo mode" in Settings shortens it).
6. Three outcomes (calm / safe but unresolved / escalation) and a **debrief** with decisions, SLOW principles,
   alternatives and suggestions.

Good choices do not guarantee a good outcome: for example, an all-aligned path can still end "safe, but unresolved"
(covered by a test). Sam's behaviour is driven by scenario state; the debrief states that autistic people differ widely.

## Architecture

```
Assets/SimSafe/
  Resources/Scenarios/*.json    Training content: nodes, options, effects, branches, criteria, objectives
  Scripts/Core/                 Pure C# (no UnityEngine): data model, ScenarioSession, ScenarioValidator, DebriefBuilder
  Scripts/Runtime/              Unity: SimSafeApp (states), WorldBuilder, Humanoid, PlayerController, UiManager, AudioSynth
  Scripts/Editor/               SimSafe menu: setup and build
  Tests/EditMode/               NUnit tests (Unity Test Runner)
tools/harness/                  Standalone .NET runner for Core + the same tests
```

* **Training data is separate from gameplay.** Add or edit a scenario by editing JSON; `ScenarioValidator` reports
  broken references, unreachable nodes and soft-locks. Learning criteria live in the JSON, so reviewers can change them.
* `ScenarioSession` is deterministic (no randomness, no clocks), which is what makes branching testable.
* The 3D layer only *reads* the session (flags such as `sirenOff`, stats, outcome tone) and the UI only calls into it.
  World flag names the 3D layer reacts to: `sirenOff`, `radioLow`, `neighbourQuiet`, `backupArrived`, `secondOfficerHandled`.
* Node `stage` values understood by the 3D layer: `near_sam` (officer walks slowly to a respectful distance), `dana_comes`.
* No backend, accounts or network access. Settings use `PlayerPrefs`.

## Tests

* **In Unity:** Window > General > Test Runner > EditMode > Run All (`SimSafe.Tests`). *Not yet run.*
* **Outside Unity:** `cd tools/harness && dotnet run`. This compiles the real Core sources and the real
  `SessionTests.cs` against a minimal NUnit shim. **28 passed, 0 failed.**
  The suite checks data validity, every branch listed above, stat clamping, one-shot options, environment interactions,
  restart, debrief and criterion rules, and explores the whole state space for soft-locks and reachability of all three outcomes.
  Breaking the data (a bad branch target, a changed threshold) makes the relevant tests fail.
  The harness does not touch any UnityEngine code.

## Not done / next steps

* **Open it in Unity, fix first-compile issues, and look at it.** Tune lighting, scale, UI layout and audio levels.
* Replace primitive characters/house/car with licensed models and animation clips; replace synthesised audio with recordings.
  (Primitive people and procedural body language are stand-ins.)
* Only one scenario and one illustrative Sam profile. Adding variants (different communication styles and sensory
  profiles) would avoid implying that autistic people respond identically.
* Content review: every line of scenario text, the criteria, and the thresholds need NASC review before use.
* Accessibility pass (screen-reader support, high-contrast theme, captions for audio cues, controller support).
* Web performance profiling (shadows, draw calls, memory) has not been done.
* PlayMode/UI smoke tests, and a CI build pipeline.
