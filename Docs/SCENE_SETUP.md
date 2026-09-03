# Scene setup — step by step

This guide builds the single-scene VR app. Everything is a World-Space canvas in
front of the player; there is no floor teleport gameplay, so setup is light.

## 0. Rig

1. Add the **Meta XR** camera rig (`OVRCameraRig` or the Interaction SDK rig prefab).
2. Add both **controller** and **hand** interactors (ray + poke) so Touch and Hand
   Tracking both work. Enable Hand Tracking in **OVRManager ▸ Hand Tracking Support =
   Controllers And Hands**.
3. Add an **EventSystem** with a `StandaloneInputModule` (for the editor fallback path).

## 1. Bootstrap object

Create empty `Bootstrap`, add (order does not matter, all self-init in Awake):

- `SudokuPuzzleBank` — leave *Prefer Streaming Assets* on if you baked the bank.
- `ProfileManager`
- `SettingsManager`
- `GoogleAnalyticsManager` — paste Measurement ID + API secret, or leave blank.
- `LocalAnalytics` — drag the `GoogleAnalyticsManager` into its `ga4` field.
- `AudioManager` — assign the ambient music clip and the 9 SFX clips.
- `GameManager`

## 2. Canvas

1. `GameObject ▸ UI ▸ Canvas`. Set **Render Mode = World Space**.
2. Scale it small, e.g. `RectTransform` size 1600×1000, `localScale` ≈ 0.0009 so it's
   ~1.4 m wide at 1.6 m distance.
3. Add `CurvedSpatialCanvas` (assign the head/Camera; it auto-finds `Camera.main`).
4. Add a `GraphicRaycaster` (and, for SDK ray input, the Interaction SDK's
   canvas/pointer components per Meta docs).

## 3. Screen panels

Add 7 child panels (empty `Image` containers), one per screen:

`BootPanel`, `ProfileSelectPanel`, `MainMenuPanel`, `SudokuPanel`,
`TicTacToePanel`, `StatsPanel`, `SettingsPanel`.

Add a `UIManager` component (put it on the Canvas or Bootstrap) and drag each panel
into the matching field. `GameManager` toggles them via `OnScreenChanged`.

## 4. Boot panel

- A `Slider` → `UIManager.bootProgressBar` (shows bank build progress).
- A `Text` → `UIManager.bootStatusLabel`.

## 5. Tic-Tac-Toe panel

1. Make a 3×3 grid (a `GridLayoutGroup` of 9 buttons).
2. On each cell: `Image` (background) + child `Text` (mark) + optional `Image`
   (win highlight, disabled). Add `TicTacToeCellView`, assign those refs, and a
   `VRButton`. Wire `VRButton.OnPressed → TicTacToeCellView.HandlePress`.
3. Drag the 9 `TicTacToeCellView`s into `UIManager.tttCells` **in index order 0..8**
   (row-major: top-left = 0, bottom-right = 8).
4. Add a status `Text` → `UIManager.tttStatusLabel`.
5. Add menu buttons calling `StartTttEasy/Medium/Unbeatable/PassPlay`, plus
   `RestartTtt` and a back button → `NavMainMenu`.

## 6. Sudoku panel

1. Make a 9×9 grid of 81 cells. Each cell: `Image` bg + child `Text` (big value) +
   9 small `Text` note labels (a 3×3 mini-grid) + optional selection ring `Image`.
   Add `SudokuCellView`, assign refs, add a `VRButton` wired to `HandlePress`.
2. Drag the 81 views into `UIManager.sudokuCells` **in index order 0..80** (row-major).
3. Number pad: 9 buttons → `SudokuInput1..9`. Control buttons → `SudokuErase`,
   `SudokuUndo`, `SudokuRedo`, `SudokuToggleNotes`, `SudokuHint`, `NewSudokuPuzzle`,
   `SudokuBackToMenu`.
4. Labels: timer → `sudokuTimerLabel`, difficulty → `sudokuDifficultyLabel`,
   hints → `sudokuHintsLabel`, notes state → `notesToggleLabel`.
5. Difficulty menu buttons → `StartSudokuEasy/Medium/Hard/Expert`. Optional resume
   button → `ResumeSudoku`.

## 7. Profile select panel

- Up to 4 slot rows. Each row: a name `Text` (→ `profileSlotLabels[i]`), a select
  `VRButton` → `SelectProfileSlot{i}`, a delete `VRButton` → `DeleteProfileSlot{i}`.
- An `InputField` → `newProfileNameField` (used when tapping an empty slot).
- Avatars: reuse `avatarIndex` (0..3 by default) to index a sprite set you assign in
  your own row prefab.

## 8. Stats panel

- A multiline `Text` → `UIManager.statsBodyLabel`.
- An "Export" `VRButton` → `ExportStats`. A back button → `NavMainMenu`.
- Entering this screen auto-calls `RefreshStats()`.

## 9. Settings panel

Wire the widgets' change events to `UIManager`:

| Widget | Event → method |
| --- | --- |
| Master volume `Slider` | `OnMasterVolume` |
| Music volume `Slider` | `OnMusicVolume` |
| SFX volume `Slider` | `OnSfxVolume` |
| Haptics `Toggle` | `OnHaptics` |
| Telemetry `Toggle` | `OnTelemetry` |
| Auto-check `Toggle` | `OnAutoCheck` |
| Theme `Toggle` (on = Light) | `OnThemeToggle` |
| Hand-tracking `Toggle` (on = Hands) | `OnHandTrackingToggle` |

Also drag these into the matching `UIManager` fields so the panel shows current values
when opened (`PullSettingsIntoWidgets` uses `SetValueWithoutNotify`).

## 10. Themes

Create two palettes via **Assets ▸ Create ▸ TTLS ▸ UI Theme Palette** (Dark & Light),
tune colours, and drag them into `UIManager.darkPalette` / `lightPalette`.

## 11. Main menu

Buttons: **Sudoku** and **Tic-Tac-Toe** (open sub-menus or difficulty pickers),
**Stats** → `NavStats`, **Settings** → `NavSettings`, **Switch Profile** →
`NavProfileSelect`.

## 12. Play

Press Play. First launch loads the baked bank (or builds it with a progress bar),
shows Profile Select, and you're in. In the Editor you can click everything with the
mouse via the `GraphicRaycaster` fallback.
