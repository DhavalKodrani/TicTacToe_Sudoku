# Native Unity build → Meta Quest Store (APK)

This is the "real native title" path for the C# project in `Assets/`. It produces
a signed Android APK/AAB for the Quest 3 and submits it to the Meta Horizon Store.
Unlike the WebXR build, **you must do these steps on your own machine** — a native
build requires Unity + the Android toolchain, and Unity scenes/prefabs must be
authored in the Editor (they're binary; they can't be generated as text files).

Budget: ~1–2 hrs first-time setup + scene assembly, then Meta's review (days).

---

## 0. Install the toolchain

1. **Unity Hub** → install **Unity 2022.3 LTS** with the **Android Build Support**
   module (includes **OpenJDK**, **Android SDK**, **NDK** — tick all three).
2. Create/open a 3D (URP) project, then copy this repo's `Assets/` into it.
3. **Package Manager** → install the **Meta XR SDK** (Meta XR Core SDK + **Meta XR
   Interaction SDK**) — from the Unity Asset Store or the Meta Hub package.

## 1. Configure the project for Quest

- **File ▸ Build Settings** → switch platform to **Android**.
- **Edit ▸ Project Settings ▸ XR Plug-in Management** → Android tab → enable **Oculus**.
- **Player Settings ▸ Android**:
  - Company/Product name; **Package name** e.g. `com.yourstudio.ttls`.
  - **Scripting Backend = IL2CPP**, **Target Architectures = ARM64** (untick ARMv7).
  - **Minimum API Level 32+** (check Meta's current minimum), Target = highest installed.
  - **Color Space = Linear**, **Graphics API = Vulkan** (or OpenGLES3), Orientation = Landscape.
  - Add **Scripting Define Symbol** `OVR_PLUGIN_PRESENT` so `VRHaptics` uses `OVRInput`.
- Run **Meta ▸ Tools ▸ Project Setup Tool** and **apply all** recommended fixes.

## 2. Build the scene

Follow **`Docs/SCENE_SETUP.md`** step by step:
- Add the Meta camera rig with **both** controller and hand interactors.
- Add the bootstrap object with all managers (`GameManager`, `ProfileManager`,
  `SudokuPuzzleBank`, `SettingsManager`, `LocalAnalytics`, `GoogleAnalyticsManager`,
  `AudioManager`).
- Build the World-Space canvas + `UIManager`, wire the 9 + 81 cells, number pad,
  menu/settings/stats/profile widgets, and connect Interaction SDK
  `InteractableUnityEventWrapper.WhenSelect` → `VRButton.Press()` / cell `HandlePress()`.

**Bake the 500 puzzles** into the APK: run **Tools ▸ TTLS ▸ Bake Sudoku Bank (500)**
so `Assets/StreamingAssets/sudoku_bank.json` ships inside the build (instant,
offline first launch).

## 3. Test on the device

1. On the Quest: enable **Developer Mode** (Meta Horizon phone app → Devices →
   Developer Mode), connect via USB, accept the "Allow USB debugging" prompt.
2. Unity **Build Settings ▸ Build And Run** (or **Build** an APK and `adb install`).
3. Verify against **`Docs/VRC_COMPLIANCE.md`** — the pre-submission smoke test at
   the bottom covers profiles, AI, Sudoku, settings persistence, offline telemetry
   flush, and stats export.

## 4. GA4 credentials

In the `GoogleAnalyticsManager` inspector, paste your **Measurement ID**
(`G-XXXXXXXXXX`) and a **Measurement Protocol API secret** (GA4 Admin ▸ Data
Streams ▸ your stream ▸ Measurement Protocol API secrets). Leave blank to ship
with telemetry disabled. Toggle **Debug** once to validate events, then turn it off.

## 5. Package for the store

- For the store, produce a **signed** build. Create a keystore
  (**Player Settings ▸ Publishing Settings ▸ Keystore Manager**), set key + passwords,
  and **keep the keystore safe** (you need the same key for every future update).
- Meta accepts **APK** uploads for Quest. Increment **Bundle Version Code** on every
  upload.

## 6. Submit to the Meta Horizon Store

1. **developer.oculus.com** → create/verify an **organization** (a store submission
   needs an organization with payment/tax set up).
2. **Create a new app** → target **Meta Quest** → app type **Native (Android)**.
3. **Upload builds** to a release channel (start with **ALPHA**, add your own test
   accounts) → move to **BETA** → then **Production/Store**.
4. **Data Use Checkup (DUC):** declare exactly what you collect. This app collects
   only anonymized, opt-in analytics — declare that honestly; the anonymized-UUID
   design keeps this simple.
5. **Store listing:** icon, cover, hero, trailer, screenshots (record in-headset),
   descriptions, IARC age rating, **privacy policy URL**, supported controllers +
   **hand tracking**, offline support, comfort rating (Comfortable).
6. **Submit for review.** Address any VRC failures Meta reports and resubmit.

> **App Lab vs. main store:** you can also release via **App Lab** (same submission
> flow, distributed by direct link/search) with a lighter bar — a good way to ship
> and gather feedback before pursuing full store featuring.

## 7. Updates
Bump the version code, rebuild with the **same keystore**, upload to a channel,
and promote through review. Keep the keystore and your GA4 secret backed up.

---

### Why I can't hand you a finished APK from here
Building a native Quest app needs the Unity Editor, the Android SDK/NDK, a signing
keystore, and interactive scene authoring — none of which exist in this
environment, and Unity scene/prefab assets are binary formats that can't be written
as source text. The complete, commented C# for every system is in `Assets/`; this
guide plus `Docs/SCENE_SETUP.md` is the path to turn it into the installable title.
