# Realistic scene and character integration plan

Status: proposed visual workstream. No models, textures, animation packages,
prefabs or replacement scenes are implemented by this document. The existing
procedural scene remains the working fallback until a replacement is approved
and passes runtime checks. Target: Unity 6000.6.6f1, URP 17.6, Windows and Web.

## Visual target and asset specifications

Build one believable southwest Florida residential frontage before expanding the
neighborhood: single-story stucco home, attached garage, driveway, sidewalk,
lawn, plain mailbox, streetlight, palms/live oak, planting beds and parked generic
patrol vehicle. Use the generated environment reference as visual direction only;
it supplies neither a 3D asset nor proof of an achievable runtime frame rate.

Use metre scale, coherent texel density and physically plausible roughness rather
than exaggerated bloom or saturated lighting. Add restrained driveway staining,
expansion joints, weathered stucco, chipped curb edges, grass variation and leaf
litter. Imperfections must not obscure traversable space or interactions. Verify
the composition at the officer's existing 1.62 m camera height and 68 degree FOV.

| Asset | Required integration data | Acceptance evidence before purchase/import |
|---|---|---|
| House/frontage | Modular exterior, sensible wall/door scale, porch and garage, simple collision meshes, separate door pivot and interaction socket | URP material demonstration, real dimensions, texture inventory, mesh/LOD counts and desktop/browser license rights |
| Road/sidewalk/driveway | Consistent curb/step heights, smooth walkable collision, tiled PBR textures, road edge transition | Test with existing CharacterController, no collision snagging or visual gaps |
| Vegetation | Species appropriate to the setting, LODs, limited transparent foliage layers, simple trunk collision | URP-compatible shaders with source/documentation, wind fallback and overdraw measurements on target hardware |
| Patrol vehicle | Generic body without unlicensed logos, realistic scale, lightbar sockets, driver-side control focus, audio emitter anchors, simple collision | Model/material/texture rights, LODs, interiors only where visible; siren and light components separately addressable |
| Humanoid | Licensed realistic adult, Unity Humanoid-compatible skeleton, clean rest pose, skinned mesh, mouth/jaw and blink shapes, neutral expression | Asset-specific redistribution terms, character likeness rights, facial blendshape inventory, tested URP skin/eye/hair materials |
| Animation | Idle, walk, turn, stop, speaking gestures, request-space gesture and sit/stand if used by scenario | Retarget preview, foot contact and transitions; clips selected per individual reviewed profile, not a universal autism portrayal |
| Audio | Licensed ambience, mower, radio, siren, dialogue fallback; mono positional sources where appropriate | Recording/voice rights, loop quality, attenuation preview and controllable mixer levels |

ARKit-style facial coverage can help future animation, but is not mandatory for
the first mouth-driven character. At minimum require independently controllable
mouth shapes suitable for the chosen lip-sync mapping, a stable closed-mouth rest
shape, blinking and eyes. Calibrate profiles against the chosen voice. A facial
rig does not supply approved behavior, dialogue, training criteria or diagnosis.

## Existing integration seams and constraints

These observations come from the current runtime source, not a new scene test:

- `WorldBuilder.Build()` constructs the world and returns a `WorldRig` consumed
  by `SimSafeApp`. It supplies spawn positions, character references, sound
  sources and police lights. Preserve these semantics through a future binder.
- `WorldRig` currently holds concrete `Humanoid` references for `Sam`, `Dana`,
  `Neighbour` and `Backup`. `Humanoid` is sealed and generates its own geometry;
  an imported skinned prefab is not a drop-in substitute. A later narrow
  character presentation contract must retain `Teleport`, `MoveTo`, `Stop`,
  `SetPose`, `IsMoving`, `Sit`, `CoverEars` and `FaceTarget` behavior where used.
  Keep the procedural implementation as an adapter until migration tests pass.
- `WorldRig.Sync(ScenarioSession)` derives current sound, light and character
  presentation from existing flags/stats. Preserve that flow and its outcome
  routes while changing only presentation. Do not let animation callbacks change
  ScenarioSession or debrief results.
- `SimSafeApp.Awake()` unconditionally calls `WorldBuilder.Build()`.
  `IsolateFromSceneContent()` disables existing scene cameras and lights. A
  realistic scene cannot simply be added beside the current scene: a later
  explicit scene-provider/binder selection must choose the replacement and
  disable only fallback content, without creating duplicate cameras/listeners.
- `Gfx` caches runtime primitive materials using saved Resources base materials.
  Imported assets should use authored shared URP materials; do not route an
  entire realistic scene through per-object `Gfx.Lit()` material creation.
- `Interactable` lives in `PlayerController.cs`. Keep `CharacterId` values
  `sam`/`dana` and object interaction IDs `car_siren`, `car_radio`,
  `neighbour_mower`. Current ranges are 4.5 m for characters and 3.5 m for
  objects; the ray ignores player layer 2. Place proxy colliders where focus
  makes sense and test occlusion before adjusting ranges.
- `WorldRig.Sync` currently couples `CarLights.On` to `sirenOff`. Independent
  visual sockets do not authorize changing that training behavior; decouple it
  only in a separately tested interaction change.

## Proposed prefab contract

The following hierarchy and component names are proposed, not existing APIs:

```text
ResidentialEnvironment
  Geometry / House / Garage / Driveway / Street / Sidewalk
  Vegetation / Props
  Collision
  Anchors
    PlayerSpawn
    SamA / SamB / PorchSeat / PorchDoor / RoadPoint
    DanaHome / NeighbourA / NeighbourB / BackupSpawn
  Vehicles
    PatrolVehicle
      LightbarRed / LightbarBlue
      SirenEmitter / RadioEmitter / ControlFocus
    BackupVehicle
  Characters
    Sam / Dana / Neighbour / Backup
      MotionRoot / VisualRig / GazeTarget / VoiceEmitter
  Lighting / ReflectionProbes / LightProbes
  AudioZones / AmbientEmitter / MowerEmitter
  EnvironmentBindings
```

Use stable binding fields, not scene-name searches. A future validation pass must
reject missing/duplicate IDs, absent anchors, unusable colliders and missing
audio/light references before a scenario starts. Keep authored local positions;
do not encode scenario rules in imported prefab scripts.

Initial anchor parity for a migration comparison, in existing world metres:

| Binding | Existing value |
|---|---|
| PlayerSpawn / yaw | (-3.2, 0.1, -7.3), 40 degrees |
| Patrol / backup vehicle position | (-4, 0, -10) / (-12.5, 0, -10), yaw 90 degrees |
| SamA / SamB | (3.2, 0, -3.8) / (6.2, 0, -3.8) |
| PorchSeat / PorchDoor | (1.4, 0.2, 6.5) / (0, 0.2, 7.6) |
| RoadPoint / DanaHome | (4, 0, -10.5) / (-1.4, 0.2, 6.8) |
| NeighbourA / NeighbourB | (-16, 0, 1.5) / (-11, 0, 1.5) |

The imported house need not duplicate the procedural shape, but moving anchors
requires a route/collision review. Positive/mixed outcome paths contain additional
hard-coded waypoints in `WorldRig.ApplySam`; migrate those to explicit anchors
only when covered by a comparison test. Preserve safe separation and accessible
walking surfaces, not merely a similar screenshot.

## Lighting, audio and presentation

Use one daylight sun and a calibrated sky, light/reflection probes and baked
static lighting where practical. Verify skin exposure and readable faces under
tree/porch shade. Keep reasonable daylight contrast; avoid a second sun or
excessive postprocessing. Retain scene lighting ownership in the selected
provider rather than relying on global bootstrap disabling.

Use distinct ambient, voice, radio, siren and mower channels with bounded levels.
The existing sources have scenario-controlled volume/playback; retain those
controls and use spatial attenuation for local sources. Label any displayed
sensory measurements as game settings, not real-world calibrated decibels.
Sound-zone entry may later emit observed events, but do not add scoring from
unreviewed zone thresholds. Validate browser audio separately from Windows.

Drive body motion through Animator parameters and authored transitions. Use a
separate lip-sync driver for mouth shapes and a bounded expression/gaze layer
so both do not fight for the same blendshape weights. Record actual action
completion/interruption when integrated; provider intentions are not evidence
that an animation or movement actually occurred. Movement should use validated
walkable destinations, cancellation and collision avoidance before replacing
the existing waypoint controller.

## Proposed quality budgets and measurement gate

These are starting targets for the one-frontage prototype, not measurements or
claims about the current project. Confirm representative Windows/browser hardware
before making them acceptance thresholds.

| Area | Windows starting target | Web starting target |
|---|---|---|
| Frame pacing | 60 fps at 1080p (16.7 ms frame budget) | 30 fps at 720p (33.3 ms), scale upward after measurement |
| Character geometry | One foreground character, LOD0 about 50k-80k triangles | Foreground LOD around 25k-40k triangles; lighter hair/materials |
| Texture policy | Mostly 2K, 4K only for justified hero surfaces | Mostly 1K, selected 2K hero surfaces; platform compression verified |
| Lighting | One shadowed sun, baked static indirect, constrained shadow distance | One shadowed sun or reduced-shadow profile; fewer cascades/probes |
| Vegetation | Instancing/LODs, bounded wind and shadow casters | More aggressive LODs and reduced alpha overdraw/shadow casters |
| Facial work | One active analysis/animated face first | One active face, test output clip readability and browser audio sync |

The repository already has PC and Mobile URP quality assets. Build on those
profiles instead of replacing global settings or importing another project's
pipeline assets. Measure CPU/GPU frame time, managed/native memory, draw calls,
SetPass calls, skinning and foliage overdraw; establish download/build size after
assets are selected. Profile worst views near foliage, car windows and the face,
not only an empty street view. Keep AI-inference timing separate from rendering.

## Licensing and approval gate

Before any import, record asset/vendor URL, exact version, license file, purchaser
or organization entitlement, redistribution permissions, third-party notices,
model/voice rights and platform restrictions in an asset register. Root repository
licenses do not automatically cover sample characters, logos, textures, motions,
native SDKs or AI model weights. Do not import paid/restricted assets or packages
without approval. Prefer a minimal approved package without samples; review
installer/editor scripts for automatic downloads and external actions.

## Small migration sequence and acceptance

1. Approve one frontage asset set and one facially rigged character from an
   inventory with licensing and compatibility evidence. Import nothing yet.
2. Make a separate candidate scene/prefab and a small explicit binder/provider;
   keep Main and the procedural provider selectable and unchanged by default.
3. Match anchors, collision and interaction IDs. Exercise reset, restart, menu
   backdrop, pause and outcome routes against both providers.
4. Replace the environment alone, validate lighting/audio and first-person
   traversal, then replace one character via the presentation adapter. Do not
   bundle behavior/scoring changes into either visual migration.
5. Add calibrated mouth animation with a prerecorded/offline response first;
   verify neutral mouth on silence, interruption and restart, eye/gaze bounds,
   gesture transitions and feet/contact.
6. Run Core/EditMode tests and Play Mode interaction checks, then Windows and Web
   builds. Check missing/pink materials, stripped shaders, colliders, audio
   ownership, routes, occlusion, performance and scene-event parity.
7. Present side-by-side first-person captures and measured results for approval.
   Switch the default only after parity and visual review; retain rollback.

Visual realism is achieved only when the imported scene/character are visible,
interactive and measured in the target application. The generated reference,
this plan and the current primitives do not meet that completion criterion.
