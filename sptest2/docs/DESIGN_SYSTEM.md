# SimSafe UI patterns

The existing UIK palette, uGUI font and spacing remain the visual baseline.

## Offline character interaction

- Use the existing HUD Canvas and EventSystem. Keep noninteractive HUD graphics
  and character captions click-through so they cannot block mouse capture.
- E opens the text composer; while open, it owns keyboard/mouse input. Do not
  process player movement, pause hotkeys or numbered scenario choices from text.
- Focus the bounded single-line field after the opening frame. Send/Enter commits
  one message; Escape/Close discards it. Tab/Shift+Tab follows explicit focus order.
  Focused buttons own Enter; an IME confirmation must not submit a turn.
- Disable rich text for input/caption content. Show empty-input disabled state and
  a visible keyboard focus tint. Keep typed text out of logs and event snapshots.
- Suspend movement only during composition or existing modal screens. Nonmodal
  response captions follow Sam's head and do not take camera or movement ownership.
- Label WAIT, REQUEST SPACE and SPEAK in text rather than color alone. WAIT timing
  comes from the core coordinator, not a UI coroutine. Caption expiration is only
  presentation and is not speech-completion evidence.

These are implementation rules, not a completed accessibility audit. Keyboard,
IME, reduced-resolution layout and actual player input still need Play Mode checks
described in OFFLINE_IN_SCENE.md.
