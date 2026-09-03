# Meta Quest VRC compliance checklist

How this codebase supports Meta's Virtual Reality Checks (VRCs) for store
submission. Items marked **[scene]** are things you finish in the Unity scene /
project settings; **[code]** is handled by the scripts here.

## Performance & comfort
- **[code]** No per-frame heap allocations in gameplay hot paths (fixed arrays,
  struct undo stack, `stackalloc`, no LINQ in loops) → stable framerate, low GC.
- **[code]** 500-puzzle generation runs on a **background thread** with a boot
  progress bar → no main-thread hitch, no frozen frames.
- **[code]** Event-driven rendering; timer text throttled to ~4 Hz.
- **[scene]** Target **72/90 Hz**, use fixed foveated rendering, keep draw calls low.
  A static curved canvas avoids locomotion-induced motion sickness entirely.
- **[code]** `CurvedSpatialCanvas` places UI at a comfortable focal distance and only
  recenters after a large head turn (no UI "swimming").

## Input
- **[code/scene]** Supports **both** Touch controllers and Hand Tracking via the
  Meta Interaction SDK (ray + poke), wired through `VRButton`/cell views.
- **[code]** Haptic feedback on every button press (`VRHaptics`, gated by a user toggle).
- **[scene]** Provide both controller and hand interactors on the rig; the app never
  requires one specific input method.

## Audio & visual clarity
- **[code]** High-contrast **Dark/Light** palettes tuned for lens readability.
- **[code]** Distinct audio cues for move / place / error / win / lose / draw / hint,
  with user volume sliders (master/music/SFX).

## Data, privacy & security
- **[code]** All game data is stored **locally** in the app's private
  `persistentDataPath` (no external storage permission needed).
- **[code]** Atomic writes + `.bak` recovery prevent save corruption.
- **[code]** GA4 telemetry is **opt-in per profile**, uses an **anonymized GUID**
  (never a hardware/controller ID), sets `non_personalized_ads:true`, and collects
  **no PII**. Disabling the toggle stops all collection immediately.
- **[code]** Works fully offline; telemetry queues locally and flushes only when
  connectivity is available.
- **[scene]** Do **not** request unnecessary Android permissions. No microphone,
  camera, or location is used. Declare only what the Meta XR SDK requires.

## Lifecycle
- **[code]** `OnApplicationPause`/`OnApplicationQuit` end the session, flush playtime,
  and persist stats → correct behaviour when the user removes the headset.
- **[code]** In-progress Sudoku is saved on exit and can be resumed.

## Store content
- **[scene]** Supply required store assets (icon, cover, screenshots, an offline
  privacy policy stating telemetry is optional/anonymous) — content task, not code.

## Pre-submission smoke test
1. Fresh install → boot builds/loads bank → Profile Select appears.
2. Create 4 profiles; confirm each has isolated stats.
3. Play each Tic-Tac-Toe mode; verify Unbeatable never loses; W/L/D recorded per mode.
4. Play each Sudoku difficulty; verify notes, undo/redo, hints, error highlight,
   completion time + streak recorded.
5. Toggle every setting; confirm it persists across a relaunch.
6. Airplane mode → play a session → re-enable network → confirm the GA4 queue flushes
   (use Debug mode to watch validation responses).
7. Export stats; confirm a formatted JSON file appears under `TTLS_Save/exports/`.
