# WebXR build — run on Quest 3 & publish to the Meta Horizon Store

This folder is a complete, self-contained **WebXR PWA**. It runs immersively on a
Quest 3 through the headset's browser and can be installed to the Quest home and
submitted to the **Meta Horizon Store** as a web app.

```
WebXR/
├─ index.html            the whole game (three.js + all logic)
├─ manifest.webmanifest  PWA manifest (name, icons, display)
├─ sw.js                 service worker (offline caching)
└─ icons/               192 / 512 / maskable app icons
```

WebXR **requires HTTPS** (or `localhost`). You cannot test immersive VR from a
`file://` path — you must serve it over HTTPS. The fastest free HTTPS host is
GitHub Pages (below). three.js is loaded from a CDN and cached by the service
worker, so after the first load the app works fully offline.

---

## A. Put it on your Quest 3 in ~5 minutes (GitHub Pages)

1. **Make this a git repo and push it to GitHub** (from the project root):
   ```bash
   cd "C:/Users/dhava/Documents/GitHub/TicTacToe_Sudoku"
   git init
   git add .
   git commit -m "TicTacToe Love Sudoku"
   ```
   Create an empty repo on github.com (e.g. `ttls-vr`), then:
   ```bash
   git remote add origin https://github.com/<you>/ttls-vr.git
   git branch -M main
   git push -u origin main
   ```
2. **Enable Pages:** GitHub repo → **Settings ▸ Pages** → Source = *Deploy from a
   branch* → Branch = `main`, folder = `/root` → Save. After ~1 min you get a URL
   like `https://<you>.github.io/ttls-vr/`.
3. **Your app URL** is that base + the WebXR path:
   `https://<you>.github.io/ttls-vr/WebXR/index.html`
   > Tip: if you'd rather the URL be the repo root, move the contents of `WebXR/`
   > to the repo root (so `index.html`, `manifest.webmanifest`, `sw.js`, `icons/`
   > sit at top level). PWAs are happiest served from the site root.
4. **On the Quest 3:** open the **Meta Quest Browser**, go to that URL, and press
   **Enter VR**. Point with a controller (or your hand) and pull the trigger /
   pinch to play.
5. **Install to Home (PWA):** in the Quest Browser, open the **⋮ / tabs** menu and
   choose **Install** / **Add to Home**. It becomes a launchable app icon that
   opens straight into the experience.

### Local testing on your PC (no headset)
```bash
cd "C:/Users/dhava/Documents/GitHub/TicTacToe_Sudoku/WebXR"
python -m http.server 8777
```
Open `http://localhost:8777/index.html` and click the panel with the mouse
(`localhost` counts as a secure context, so the app loads; immersive VR still
needs a headset). Use Chrome's **WebXR emulator** extension to fake a headset.

---

## B. Publish to the Meta Horizon Store (WebXR PWA)

Meta distributes WebXR apps as **Progressive Web Apps** through the Meta Horizon
Store. The exact dashboard wording changes over time, so treat this as the shape
of the process and follow Meta's current *"Publish a PWA / Web app"* docs.

### 1. Prerequisites
- A **Meta Horizon / Quest developer account** (developer.oculus.com) with a
  verified organization (needs a payment method / D-U-N-S for an org, or you can
  publish as an individual developer).
- Your app **hosted on HTTPS** at a stable URL (GitHub Pages is fine to start;
  a custom domain looks more professional for a store listing).
- A valid **manifest** and **service worker** (both included here) and icons at
  **192** and **512** px (included).

### 2. Create the app
1. Go to the **Meta Horizon Developer Dashboard** → create a new app → choose the
   **Web / PWA** app type (not "Native/Android").
2. Provide your **PWA URL** (the `index.html` / manifest location). Meta's tools
   validate the manifest, HTTPS, offline capability, and that it launches into an
   immersive session.

### 3. Store listing (content you supply)
- App name: **TicTacToe Love Sudoku**, short & long description, category *Games/Puzzle*.
- **Icon** (from `icons/icon-512.png`), cover art, and **screenshots/short video**
  captured in-headset (record via the Quest share menu).
- **Privacy policy URL** stating telemetry is optional & anonymized (see below),
  age rating (IARC questionnaire), and supported input (controllers + hands).

### 4. Pass the Web VRC checks
Meta reviews web apps against Virtual Reality Checks. This build already helps:
- **Immersive entry** via the standard WebXR `Enter VR` button.
- **Both inputs**: controllers (ray + trigger) *and* hand tracking (pinch).
- **Comfort**: a fixed, curved, eye-height panel — no forced locomotion.
- **Offline**: the service worker caches the app + three.js.
- **Performance**: single-canvas UI texture, minimal draw calls; targets Quest framerate.
- **Privacy**: no PII; anonymized per-profile ID; a telemetry opt-out in Settings.

### 5. GA4 for the web build (optional)
The prototype logs telemetry to the console. To send real GA4 events, replace the
`Tel` object's `flush()` with a `fetch()` to the GA4 Measurement Protocol
(`https://www.google-analytics.com/mp/collect?measurement_id=…&api_secret=…`) using
the same event shapes already produced (`screen_view`, `game_start`,
`game_complete`, `setting_changed`). Keep the offline queue (already implemented)
so events buffer when the headset is offline.

### 6. Submit
Upload the listing, run Meta's automated validation, then submit for review.
Fix any flagged items and resubmit. Approved web apps appear in the store and can
be installed to the Quest home like native titles.

---

## Updating the app
Edit the files, bump `CACHE` in `sw.js` (so headsets fetch the new version), and
push again. GitHub Pages redeploys automatically; the store listing updates when
you re-validate the URL in the dashboard.

## Visual style
The build is a bright, warm **tabletop diorama** ("Angry Birds on Quest" feel):
a wooden play table on a grassy island under a sunny sky, with a **real 3D
Tic-Tac-Toe board** (chunky blue X / red O pieces that drop, bounce and cast soft
shadows) and a **physical Sudoku tray** with a chunky floating number pad. Menus,
stats and settings are wooden signboards. Controllers, hand tracking, haptics,
confetti and audio cues are all wired in.

## Note on this being a preview vs. a shippable title
The WebXR build is a genuine, playable immersive app and a legitimate store path.
For a polished commercial release you'll likely want to: bundle three.js locally
instead of via CDN (for guaranteed offline first-load and store review), add
spatialized 3D audio, add grab-and-place physics on the pieces, textured wood
materials, and expand the puzzle bank. Everything here is structured to grow into that.
