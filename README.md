# TicTacToe Love Sudoku — Meta Quest (Unity)

A complete, **offline-first** VR puzzle game for the Meta Quest store combining a
Tic-Tac-Toe engine (with an unbeatable MiniMax AI) and a full Sudoku engine
(500 validated puzzles, notes, undo/redo, hints), wrapped in a spatial VR UI with
per-profile local data, offline KPI tracking, and optional GA4 telemetry.

Everything runs **without an internet connection**. Telemetry is opt-in, queued
offline, and flushed when connectivity returns.

---

## Two ways to run this on Quest

This repo contains **two complete implementations of the same game**, each with its
own path onto a Quest 3 and into the Meta store:

| Build | Folder | What it is | Store path |
| --- | --- | --- | --- |
| **WebXR (immersive, ready now)** | [`WebXR/`](WebXR/) | A true VR app (three.js) that runs in the Quest Browser — Enter VR, controllers + hand tracking. Installable as a PWA. **Fully built & tested.** | Meta Horizon Store as a **web app / PWA** — see [`WebXR/DEPLOY_AND_PUBLISH.md`](WebXR/DEPLOY_AND_PUBLISH.md) |
| **Native Unity (this C# project)** | [`Assets/`](Assets/) | The production C# for a native APK. You assemble the scene in Unity and build. | Main Meta Quest Store as a **native APK** — see [`Docs/UNITY_BUILD_AND_PUBLISH.md`](Docs/UNITY_BUILD_AND_PUBLISH.md) |

There's also a non-VR **desktop/browser prototype** at
[`Prototype/web-preview.html`](Prototype/web-preview.html) for quickly reviewing UX.

Fastest path to your headset: follow **[`WebXR/DEPLOY_AND_PUBLISH.md`](WebXR/DEPLOY_AND_PUBLISH.md)**
(GitHub Pages → open the URL in the Quest Browser → Enter VR).

The rest of this document covers the **native Unity** build.

---

## 1. What's included

All gameplay logic is **headless C#** (no scene dependencies) so it is testable
and reusable; MonoBehaviours are thin coordinators.

| Script | Role |
| --- | --- |
| `Core/GameEnums.cs` | Shared enums (screens, game types, difficulty, marks…) |
| `Core/GameManager.cs` | Boot orchestration, navigation authority, screen_view telemetry |
| `Games/TicTacToe/TicTacToeEngine.cs` | Board state, win/draw checks, **MiniMax** (Easy/Medium/Unbeatable) |
| `Games/Sudoku/SudokuGenerator.cs` | Backtracking generator + **unique-solution** validator + solver |
| `Games/Sudoku/SudokuPuzzleBank.cs` | The **500-puzzle** bank (baked or first-launch generated, cached) |
| `Games/Sudoku/SudokuEngine.cs` | Placement/validation, pencil notes, **undo/redo**, hints, error highlight |
| `Profiles/PlayerProfile.cs` | Serializable profile / settings / stats / in-progress records |
| `Profiles/ProfileManager.cs` | Up to **4 isolated profiles**: create/delete/switch, data routing |
| `Analytics/LocalAnalytics.cs` | Offline KPI tracker + JSON export |
| `Analytics/GoogleAnalyticsManager.cs` | **GA4 Measurement Protocol** via UnityWebRequest, offline queue |
| `Settings/SettingsManager.cs` | Applies/broadcasts per-profile settings, logs changes |
| `Audio/AudioManager.cs` | Ambient music + pooled SFX with per-profile mixing |
| `VR/VRButton.cs` | SDK-agnostic button facade (poke/ray/hand + haptics + click SFX) |
| `VR/VRHaptics.cs` | Controller haptics (Meta `OVRInput` or Unity XR fallback) |
| `VR/CurvedSpatialCanvas.cs` | Ergonomic world-space canvas placement + recenter |
| `UI/UIManager.cs` | The presentation hub: binds VR input to engines, all screens |
| `UI/SudokuCellView.cs`, `TicTacToeCellView.cs` | Per-cell visuals |
| `UI/UIThemePalette.cs` | High-contrast Dark/Light palettes for lens readability |
| `Editor/SudokuBankBaker.cs` | `Tools ▸ TTLS` — bake the 500-puzzle bank into StreamingAssets |

---

## 2. Requirements

- **Unity 2022.3 LTS** (or 2021.3 LTS). Uses C# 9 features (`switch` expressions,
  `??=`, `Span`/`stackalloc`) — both LTS lines support these.
- **Meta XR SDK** (Meta XR Core SDK + **Meta Interaction SDK**) from the Unity
  Asset Store / Package Manager, OR the Oculus Integration package.
- **com.unity.ugui** (Unity UI) — included by default. Scripts use `UnityEngine.UI`
  (`Text`, `Image`, `Slider`, `Toggle`, `InputField`) so they compile with no extra
  packages. TextMeshPro is recommended for final polish (swap `Text` → `TMP_Text`).
- Android build support (Quest is Android).

> The scripts have **no hard reference** to the Meta SDK, so the project compiles
> before the SDK is imported. You connect the SDK's interactors to `VRButton.Press()`
> and the cell views in the scene (see §5).

---

## 3. First-time project setup

1. Create/open a Unity 3D (URP recommended) project and copy the `Assets/` folder in.
2. Import the **Meta XR SDK** (Core + Interaction). Run **Meta ▸ Tools ▸ Project Setup Tool**
   and apply all recommended Quest fixes.
3. **Player Settings ▸ Android**:
   - Minimum API Level 29+, Scripting Backend **IL2CPP**, Target Architectures **ARM64**.
   - Color space **Linear**, orientation Landscape.
4. **XR Plug-in Management ▸ Android** → enable **Oculus**.
5. (Recommended) Add the scripting define **`OVR_PLUGIN_PRESENT`** in
   *Player Settings ▸ Scripting Define Symbols* so `VRHaptics` uses `OVRInput`.
   Without it, haptics fall back to the generic Unity XR input path.

---

## 4. Bootstrap scene

Create a `Bootstrap` GameObject and add these components (all are `DontDestroyOnLoad`
singletons):

```
Bootstrap (GameObject)
 ├─ SudokuPuzzleBank
 ├─ ProfileManager
 ├─ SettingsManager
 ├─ GoogleAnalyticsManager   (fill Measurement ID + API secret, or leave blank)
 ├─ LocalAnalytics           (assign the GoogleAnalyticsManager reference)
 ├─ AudioManager             (assign ambient clip + SFX clips)
 └─ GameManager
```

Then a `WorldSpaceCanvas` (Render Mode = **World Space**) with `CurvedSpatialCanvas`
and one child panel per screen, plus a `UIManager` that references them.

See **`Docs/SCENE_SETUP.md`** for the full step-by-step wiring, including the
81 Sudoku cells and 9 Tic-Tac-Toe cells.

---

## 5. Wiring Meta Interaction SDK to the UI

The idiomatic Meta workflow is to expose UnityEvents and connect interactors in the
inspector — that's exactly what `VRButton` and the cell views do:

1. Put a **RayInteractable** + **PokeInteractable** (from the Interaction SDK) on each
   button/cell, backed by a collider.
2. Add an **InteractableUnityEventWrapper** and wire its **`WhenSelect`** (controllers)
   and hand-pinch select to:
   - `VRButton.Press()` for menu/number-pad/control buttons, **or**
   - `TicTacToeCellView.HandlePress()` / `SudokuCellView.HandlePress()` for board cells.
3. Add both a **controller ray interactor** and a **hand ray/poke interactor** to your
   rig so Touch **and** Hand Tracking work. The control-scheme toggle in Settings is a
   user preference/telemetry signal; both input methods remain active.

Haptics + click SFX fire automatically inside `VRButton.Press()`.

---

## 6. The 500 Sudoku puzzles

Two supported strategies (both fully offline):

- **Baked (recommended for store builds):** run **`Tools ▸ TTLS ▸ Bake Sudoku Bank (500)`**.
  It writes `Assets/StreamingAssets/sudoku_bank.json` (~0.5 MB) which ships inside the APK.
  On launch the bank loads instantly — no runtime generation.
- **First-launch generation (fallback):** if no baked bank is found, `SudokuPuzzleBank`
  generates all 500 on a **background thread** (progress shown on the boot screen) and
  caches them to `persistentDataPath`. Subsequent launches are instant.

Every puzzle is generated from a **fixed seed**, so puzzle IDs are stable across devices
and reinstalls, and each is validated to have **exactly one solution**. Use
**`Tools ▸ TTLS ▸ Validate Sudoku Generator`** for a quick uniqueness check.

---

## 7. GA4 telemetry (optional)

1. In GA4: **Admin ▸ Data Streams ▸ [your stream] ▸ Measurement Protocol API secrets**
   → create a secret. Note the stream's **Measurement ID** (`G-XXXXXXXXXX`).
2. Put both into the `GoogleAnalyticsManager` inspector fields (or call
   `SetCredentials(...)` at runtime). **Leave blank to disable** — the manager no-ops safely.
3. Enable **Debug** mode temporarily to validate events via the `/debug/mp/collect` endpoint.

**Events sent** (per spec):
- `screen_view` — `screen_name`, `page_title` (`main_menu`, `profile_select`,
  `sudoku_board`, `tictactoe_board`, `stats_dashboard`, `settings`).
- `game_start` — `game_type`, `difficulty`.
- `game_complete` — `game_type`, `time_taken_seconds`, `outcome`, `hints_used`, `difficulty`.
- `setting_changed` — `setting_name`, `new_value` (dark mode, volumes, control scheme, …).

**Offline behaviour:** every event is written to an on-disk JSON queue immediately and
flushed in batches (≤25/req) only when `Application.internetReachability` is reachable.
Failed sends stay queued and retry. The queue is capped to prevent unbounded growth.

**Privacy / VRC:** `client_id` is an **anonymized per-profile GUID** (never a hardware ID),
`non_personalized_ads:true` is always set, and a per-profile **telemetry toggle** in
Settings gates all collection. No PII is ever transmitted or stored.

---

## 8. Where data lives

All under `Application.persistentDataPath/TTLS_Save/` (the app's private Quest storage):

```
profiles/index.json          which of the 4 slots exist + last active
profiles/p0..p3.json         one isolated PlayerProfile each (settings+stats+in-progress)
sudoku/bank.json             cached 500-puzzle bank
analytics/ga4_queue.json     pending offline telemetry
exports/stats_*.json         manual KPI exports (Settings/Stats ▸ Export)
```

Writes are **atomic** (temp-file + `File.Replace`) with a `.bak` fallback, so a crash or
battery-death mid-save cannot corrupt a profile.

---

## 9. Performance / VRC notes

- Engines avoid per-move heap allocations (fixed arrays, struct undo commands,
  `stackalloc`, no LINQ in hot paths); MiniMax uses in-place make/undo with alpha-beta.
- Rendering is event-driven (only changed cells re-render); the Sudoku timer text
  updates ~4 Hz, not per frame.
- Puzzle generation runs off the main thread → no frame hitches → holds Quest framerate.
- See **`Docs/VRC_COMPLIANCE.md`** for a checklist mapped to Meta's Virtual Reality Checks.

---

## 10. Testing without a headset

`VRButton` and the cell views implement `IPointerClickHandler`, so with a
`GraphicRaycaster` + `StandaloneInputModule` you can click through every screen in the
Editor Game view before deploying to the device.
