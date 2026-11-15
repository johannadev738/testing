# SimSafe v0.2 baseline review

Historical pre-commit audit. The reviewed baseline was committed locally as
`49e8e83`; see V02_FOUNDATION.md for subsequent implementation and validation.

Audit date: 2026-10-21. Branch: `feat/simsafe-v0.2`.
This document records baseline preparation, not completion of the v0.2 milestone.

## Version control

Before preparation, only the root README was tracked (commit `e05d87b`).
The entire existing Unity project was untracked. No additional AGENTS.md or
project Obsidian index was found. No application behavior was changed during preparation.

The proposed baseline includes existing Assets with their .meta files, Packages
including packages-lock.json, ProjectSettings, the project README, and the
standalone harness. Small existing Unity template assets are retained to preserve
their references. Existing GDK generated resource assets are retained because they
are small project resources; no package or dependency is removed in this baseline.

Root and Unity ignore rules exclude credentials, local context, editor settings,
Library, Temp, Obj/bin, Logs, builds, profiler/memory captures, generated solutions
and generated C# projects. The hand-written tools/harness/Harness.csproj is
explicitly retained. Unity .meta files are retained for source assets.

See BASELINE_CONTENTS.txt for the exact proposed added files and sizes. No commit
or push has been made. Large external assets have not been imported.

## Toolchain and tests

- Unity editor: 6000.6.6f1. URP: 17.6.0. Input System: 1.20.0.
  Unity Test Framework: 1.8.0. Desktop/mobile quality profiles reference URP assets.
- Installed .NET SDK: 10.0.400. Installed .NET 8 runtime: 8.0.16.
- The harness remains net8.0, C# 9, with no third-party package references.
- Original restore failed with NU1100 for Microsoft.NETCore.App.Ref,
  Microsoft.WindowsDesktop.App.Ref and Microsoft.AspNetCore.App.Ref version
  8.0.30. Only .NET 10 reference packs were installed, and NuGet.config cleared
  all package sources. A runtime does not supply the compilation reference packs.
- Restoring those framework packs from https://api.nuget.org/v3/index.json
  succeeded. NuGet.config now includes that official feed for clean machines.
  No new SDK was installed; no target-framework change was made.
- `dotnet run --project SimSafe/tools/harness/Harness.csproj --no-restore`:
  **28 passed, 0 failed**, exit code 0. These are the existing SessionTests using
  the NUnit shim; they exercise real Core sources and scenario data, not Unity.
- Unity batch EditMode launch was attempted both within and outside the sandbox.
  Both exited with code 1 before compilation/test execution. No XML results were
  produced. The sandboxed launch reported Windows system-query assertions and
  licensing connection failure. The unrestricted launch connected to licensing
  and resolved entitlement, but still exited immediately after selecting the
  project. Its root cause remains unresolved; no Unity compilation success is claimed.
- Local Unity logs remain ignored and must not be committed or uploaded.

Reproduce from the repository root with a compatible SDK and .NET 8 runtime:

```powershell
dotnet restore SimSafe/tools/harness/Harness.csproj
dotnet run --project SimSafe/tools/harness/Harness.csproj --no-restore
```

Official references:
- https://learn.microsoft.com/dotnet/core/tools/dotnet-restore
- https://docs.unity.com/en-us/engine/6000.5/manual/scripting/test-framework-introduction/running-tests/run-tests-from-command-line

## Input and existing capabilities

UiManager.Init creates an EventSystem with StandaloneInputModule if none exists.
The Canvas has a GraphicRaycaster; UIK.MakeButton registers onClick callbacks and
automatic navigation. ShowMainMenu selects the first button. Legacy Horizontal,
Vertical, Submit and Cancel axes exist, and Active Input Handling is Both (2).
Movement, mouse look, interaction, pause and numbered dialogue choices use legacy
UnityEngine.Input. This static wiring is consistent; it does not prove input works.
An existing EventSystem is reused without validating its input module. Tab
navigation advertised by the old README is not established by this static review.

ScenarioSession, branching, one-shot environmental decisions and debrief are
covered by the passing core tests. Runtime code provides first-person movement,
raycast interactions, menus, synthetic audio and a procedurally generated world.
Siren and emergency lights share the sirenOff flag. Character interaction opens
the current dialogue node without consulting the selected CharacterId. These
findings are recorded without changing existing behavior.

## Next approved slice (not implemented)

After baseline commit approval:
1. Add Core/SimulationEvent.cs: validated IDs, event types and immutable typed
   payloads; no raw audio, transcripts, credentials or personal records.
2. Add Core/SimulationEventRecorder.cs: injected monotonic clock, unique session
   and event IDs, timestamp validation, isolated sessions and defensive snapshots.
3. Add EditMode recorder tests and include them in the standalone harness.
4. Minimally integrate session start/end boundaries and one verified successful
   environmental interaction into Runtime/SimSafeApp.cs. Preserve scenario rules.
5. Run core tests and repeat Unity validation where the editor can start.

No AI SDK, scene replacement, character behavior rewrite or new training scoring
is part of this slice. Realistic prefab environments, facial animation, voice,
movement/proximity events and explainable SLOW evidence remain later milestones.

## Required editor/manual verification

- Resolve the editor startup failure and run EditMode tests in Unity 6000.6.6f1.
- Open Assets/SimSafe/Scenes/Main.unity and inspect Console after compilation.
- Test Start Training, Scenario Selection, Settings and back navigation by mouse
  and keyboard; verify visible focus, Submit/Cancel and any advertised Tab behavior.
- Test briefing, WASD/mouse look, E interactions, cursor capture, pause/resume,
  environmental controls, dialogue waits, all outcomes, debrief and restart.
- Check existing scene rendering/audio, physics and interaction distances.
- Build and smoke-test Windows and WebGL, including browser pointer capture.
- The procedural visuals are placeholders; no visual realism, licensed character
  integration, natural voice behavior or NASC-reviewed scoring is claimed.
