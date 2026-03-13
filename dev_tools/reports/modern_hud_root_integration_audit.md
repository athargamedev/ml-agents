# Modern_HUD_Root Integration Audit

Update 2026-03-01: the duplicate `Theme/Styles` asset tree has been removed. `Theme/Content/stylesheets` is now the only canonical theme source.

## Scope

This audit covers the current UI composition rooted at `Modern_HUD_Root` in
`Assets/Network_Game/Scene/Behavior_Scene.unity`, plus the three UXML documents
bound by `ModernUISetup`.

## Ownership Split

### Visual HUD owner

`Modern_HUD_Root` should own only:

- `ModernUISetup`
- `ModernHudController`
- `DialogueEffectFeedbackPrompt`
- `CombatRuntimeOverlay`
- `DialogueDebugPanel`
- `Canvas`, `CanvasScaler`, `GraphicRaycaster`
- child `UIDocument`s:
  - `Login_Screen`
  - `Profile_Card`
  - `Dialogue_Overlay`

### Non-visual runtime services

These must remain outside the HUD root:

- `DialogueFeedbackCollector`
- `DialogueEffectFeedbackRuntimeTuner`

They are runtime services and persistence layers, not presentation.

## Style Redundancy

There are two mirrored theme trees with duplicated filenames:

- `Assets/Network_Game/UI/Theme/Styles/*`
- `Assets/Network_Game/UI/Theme/Content/stylesheets/*`

Sampled files `core.uss`, `buttons.uss`, and `inputs.uss` are byte-identical
duplicates. Current live references flow through `BlocksShared.uss`, which
imports `Theme/Content/stylesheets/*`.

### Canonical decision

- Keep `Theme/Content/stylesheets/*`
- Deprecate `Theme/Styles/*`
- Remove the duplicate tree only after all references are updated and verified

## UXML Inline Style Extraction List

Inline styles should be reduced to layout exceptions only. The following
elements currently carry heavy inline styling and should be converted to named
classes in `BlocksShared.uss` (or imported component USS files).

### PlayerLoginUI.uxml

File: `Assets/Network_Game/UI/Login/PlayerLoginUI.uxml`

1. Root login modal container
   - Element: `.blocks-modal.blocks-login-modal`
   - Current inline styles:
     - `flex-direction: column`
     - `width: 372px`
     - `height: 591px`
   - Extract to class:
     - `.hud-login-modal`

2. Login title label
   - Element: header label `PLAYER IDENTITY`
   - Current inline styles:
     - width/height overrides
     - custom padding and margin
     - `font-size: 20px`
     - `letter-spacing: 1px`
     - `transition-duration: 1s`
   - Extract to class:
     - `.hud-login-title`

3. First input group
   - Element: first `.blocks-login-input-group`
   - Current inline styles:
     - fixed `height: 87px`
     - fixed `width: 319px`
   - Extract to class:
     - `.hud-login-input-group--primary`

4. Name field
   - Element: `name-input`
   - Current inline styles:
     - `font-size: 16px`
     - min-height/min-width
     - edge margins
     - `transition-duration: 1s`
   - Extract to class:
     - `.hud-login-input`

5. Bio field
   - Element: `bio-input`
   - Current inline styles:
     - `height: auto`
     - `font-size: 15px`
     - width auto
     - edge margins
   - Extract to class:
     - `.hud-login-input--multiline`

6. Status label
   - Element: `status-label`
   - Current inline styles:
     - `border-width: 0`
     - custom muted text color
     - centered text alignment
     - top margin
   - Extract to class:
     - `.hud-login-status`

### PlayerProfileUI.uxml

File: `Assets/Network_Game/UI/Profile/PlayerProfileUI.uxml`

1. Profile card shell
   - Element: `profile-card`
   - Current inline styles:
     - all paddings forced to `1px`
     - `max-width: 250px`
     - custom `font-size`
     - `letter-spacing: 1px`
     - custom translucent background
   - Extract to class:
     - `.hud-profile-card`

2. Profile header row
   - Element: `.blocks-profile-card__header`
   - Current inline styles:
     - border width override
     - explicit `height: 20px`
     - explicit `width: 90%`
     - custom top/bottom padding
   - Extract to class:
     - `.hud-profile-card__header`

3. Player name label
   - Element: `player-name`
   - Current inline styles:
     - `font-size: 14px`
   - Extract to class:
     - `.hud-profile-name`

4. Player status label
   - Element: `player-status`
   - Current inline styles:
     - top/bottom padding
   - Extract to class:
     - `.hud-profile-status`

5. Player bio label
   - Element: `player-bio`
   - Current inline styles:
     - `font-size: 14px`
     - `border-width: 0`
     - explicit text color
   - Extract to class:
     - `.hud-profile-bio`

6. Netcode row wrapper
   - Element: anonymous row `VisualElement`
   - Current inline styles:
     - row layout
     - `justify-content: space-between`
     - `margin-top: 5px`
   - Extract to class:
     - `.hud-profile-meta-row`

7. Client id label
   - Element: `client-id`
   - Current inline styles:
     - `font-size: 14px`
     - bold weight
     - explicit margins/padding
     - explicit white color
   - Extract to class:
     - `.hud-profile-client-id`

### ModernDialogueUI.uxml

File: `Assets/Network_Game/UI/Dialogue/ModernDialogueUI.uxml`

1. Chat container shell
   - Element: `chat-container`
   - Current inline styles:
     - explicit absolute positioning
     - fixed `width: 900px`, `height: 220px`
     - border radius
     - flex setup
     - background color / opacity
     - `letter-spacing: 1px`
   - Extract to class:
     - `.hud-dialogue-shell`

2. Header row
   - Element: `.blocks-profile-card__header`
   - Current inline styles:
     - `height: 50px`
     - full custom padding
     - custom translucent background
     - `font-size: 15px`
   - Extract to class:
     - `.hud-dialogue-header`

3. Camera switch button
   - Element: `camera-switch`
   - Current inline styles:
     - explicit width/height
     - custom font size
     - explicit display
   - Extract to class:
     - `.hud-dialogue-camera-button`

4. Dialogue header text
   - Element: `dialogue-header`
   - Current inline styles:
     - `font-size: 16px`
     - explicit height and width
   - Extract to class:
     - `.hud-dialogue-title`

5. Listener status group wrapper
   - Element: anonymous right-side `VisualElement`
   - Current inline styles:
     - row layout
     - centered alignment
   - Extract to class:
     - `.hud-dialogue-header-actions`

6. Listener status label
   - Element: `listener-status`
   - Current inline styles:
     - `font-size: 12px`
     - fixed height/width
     - bold
     - right margin
   - Extract to class:
     - `.hud-dialogue-listener`

7. Close button
   - Element: `close-chat`
   - Current inline styles:
     - explicit width/height
     - `font-size: 16px`
   - Extract to class:
     - `.hud-dialogue-close`

8. Transcript scroll view
   - Element: `transcript-scroll`
   - Current inline styles:
     - all padding overrides
     - `font-size: 14px`
     - flex sizing overrides
     - `letter-spacing: 1px`
   - Extract to class:
     - `.hud-dialogue-transcript`

9. Input row
   - Element: `input-row`
   - Current inline styles:
     - row layout and alignment
     - full padding override
     - border-top
     - background color
     - font sizing
     - explicit display/visibility/overflow
   - Extract to class:
     - `.hud-dialogue-input-row`

10. Chat input field
    - Element: `chat-input`
    - Current inline styles:
      - flex sizing
      - edge margins
      - min-height, min-width
      - `font-size: 12px`
      - explicit `height: 50px`
      - `letter-spacing: 1px`
      - border overrides
    - Extract to class:
      - `.hud-dialogue-input`

11. Send button
    - Element: `send-button`
    - Current inline styles:
      - explicit width/height
      - `font-size: 15px`
      - border radius overrides
      - border width overrides
      - padding and margin overrides
    - Extract to class:
      - `.hud-dialogue-send`

## Required Follow-up Refactor

1. Create a single `hud-overlays.uss` (or similar) for component-level classes:
   - login
   - profile
   - dialogue
   - feedback toolbar
   - combat overlay
   - debug panel

2. Keep `BlocksShared.uss` as the shared token and primitive entry point.

3. Remove most geometry values from UXML and replace them with:
   - semantic classes
   - a small number of CSS-like utility classes

4. After the USS migration, re-open each `UIDocument` and verify:
   - no inline layout fights `BlocksShared.uss`
   - no duplicate width/height rules exist in both UXML and USS
   - all visible panels can be placed through a single root zone system

## DialogueClientUI Migration Boundary

`DialogueClientUI.cs` currently remains a legacy code path, but it is not
serialized on `Behavior_Scene` and was not found in any `.unity` or `.prefab`
asset under `Assets/`.

That means:

- it is not the active owner of `Modern_HUD_Root`
- it is not the current scene-bound dialogue HUD implementation
- it should be treated as legacy until either:
  - its behavior is ported into the `Dialogue_Overlay` `UIDocument`, or
  - the class is formally deprecated and removed

### Minimal migration path

1. Keep `Dialogue_Overlay` as the canonical dialogue surface.
2. Do not attach `DialogueClientUI` to new scenes.
3. Port only the remaining behavior logic that is still unique:
   - participant resolution
   - request send flow
   - transcript formatting rules
4. Re-implement those behaviors behind a UI Toolkit controller bound to the
   `Dialogue_Overlay` document.
5. Remove the legacy TMP/uGUI assumptions after parity is reached.
