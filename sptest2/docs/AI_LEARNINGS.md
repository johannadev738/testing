# Verified project learnings

- 2026-10-21: Preserve tools/harness/Harness.csproj when ignoring Unity-generated
  *.csproj files. Without an explicit exception the standalone test harness is lost
  from a clean checkout.
- 2026-10-21: .NET 8 runtimes alone do not provide net8.0 reference packs. With
  SDK 10.0.400 and only .NET 10 packs installed, an empty NuGet package-source list
  blocked harness restore. Restoring Microsoft framework packs from NuGet allowed
  all 28 existing tests to pass without retargeting the project.
- 2026-10-21: Input Handling Both matches the current legacy input code but does
  not establish functional menu input. Unity startup blocked runtime verification.
  See BASELINE.md for evidence and outstanding manual checks.
- 2026-10-21: Sandboxed process inventory can miss the interactive Unity editor.
  Read-only elevated inventory and Windows Restart Manager identified the current
  project lock owner. Do not delete an active lock or blame licensing/source from
  a terse batch exit. See V02_FOUNDATION.md.
- 2026-10-21: Unity documents incorrect realtimeSinceStartupAsDouble values in
  OnDestroy. Use an injected process Stopwatch clock for lifecycle event recording.
- 2026-10-21: Unity contains a bundled .NET SDK at Editor/Data/DotNetSdk. Its
  compiler and existing project response files can independently verify assemblies
  without touching live Library outputs, but cannot establish Play Mode behavior.
- 2026-10-21: Noninteractive HUD graphics (especially the central crosshair) must
  not receive raycasts: PointerOverUi can otherwise prevent mouse recapture.
  Composer controls remain interactive; character captions are click-through.
- 2026-10-21: A global Return handler can override a focused Close button depending
  on Update order. Route direct text submit only from the selected InputField,
  let EventSystem submit buttons, guard close-frame gameplay and IME confirmation.
- 2026-10-21: Track active processing seconds separately from recorder timestamps.
  Pause excludes active waiting but not real event time; record exact response
  conditions with immutable links to earlier submission/proximity/sensory events.
