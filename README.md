# Camledian Photobooth

A self-service photobooth kiosk for Windows — webcam capture, live preview, green screen / AI /
hybrid background removal, overlays, printing, and optional Cloudflare-backed cloud sync with QR
photo download. Offline-first: everything from camera → preview → capture → composite → save →
print works with no internet connection; the cloud is only used for config sync, photo upload, and
the QR download link.

## Project structure

```
src/
  Camledian.Photobooth.Core       Models, settings, the kiosk state machine, token generation
  Camledian.Photobooth.Imaging    HSV chroma key, image composition, output templates (net10.0, cross-platform)
  Camledian.Photobooth.Camera     ICameraProvider: WebcamCameraProvider (OpenCvSharp) + MockCameraProvider
  Camledian.Photobooth.AI         ONNX background removal (AI) + Hybrid (chroma + AI combined)
  Camledian.Photobooth.Storage    SQLite (Microsoft.Data.Sqlite), migrations, repositories, photo file layout
  Camledian.Photobooth.Printing   Print interfaces + ESC/POS receipt payload builder (net10.0, cross-platform)
  Camledian.Photobooth.Printing.Windows  Windows printing (System.Drawing.Printing) + serial (COM/Bluetooth) receipt printer transport
  Camledian.Photobooth.Cloud      Cloudflare API client, device pairing, sync queue worker, QR generation
  Camledian.Photobooth.App        WPF app (net10.0-windows) — the actual kiosk UI
tests/
  Camledian.Photobooth.Tests      xunit tests for Core/Imaging/Camera/Storage
cloud/                            Cloudflare Worker backend (TypeScript, D1, R2)
assets/
  backgrounds/, overlays/          Bundled demo backgrounds/overlay, also copied next to the built app
  branding/logo-full.png           Kiosk UI brand logo (Idle screen + Admin header) — not the same as
                                    the per-photo Branding.LogoPath, which stamps a logo onto the photo
scripts/                          setup / build / test / run / download-models / dev-cloud (PowerShell)
.github/workflows/ci.yml          .NET CI: Windows build+test — manual run, or a v* release tag
.github/workflows/cloud.yml       Cloud CI: Worker typecheck+test — manual run only
```

## Requirements

- **.NET 10 SDK** — the whole solution targets `net10.0` / `net10.0-windows`.
- **Node.js 20+** and npm — for the Cloudflare Worker (`cloud/`).
- **PowerShell 7+** (`pwsh`) to run the `scripts/*.ps1` helpers — optional but recommended; every
  command they wrap can also be run directly (shown below).
- **Windows** — only needed to actually *run* `Camledian.Photobooth.App` (real webcam, printing,
  fullscreen kiosk window). The whole solution, including the WPF app, **builds and unit-tests on
  Linux/macOS too** — see [Why the WPF app builds on non-Windows](#why-the-wpf-app-builds-on-non-windows)
  below. Full run/deploy verification of the Windows app happens in CI on `windows-latest`.

## Setup

```
./scripts/setup.ps1
```

This restores the .NET solution and runs `npm install` for `cloud/`, creating `cloud/.dev.vars`
from `cloud/.dev.vars.example` if it doesn't exist yet. Equivalent manual steps:

```
dotnet restore Camledian.Photobooth.slnx
cd cloud && npm install && cp .dev.vars.example .dev.vars
```

## Build

```
./scripts/build.ps1
```

Runs `dotnet build Camledian.Photobooth.slnx` and `npm run build` (TypeScript typecheck) in `cloud/`.

## Tests

```
./scripts/test.ps1
```

Runs the .NET test suite (`dotnet test`, xunit — state machine transitions, HSV chroma key masking,
image composition, token/pairing-code generation, SQLite repositories, the sync queue's backoff
math, `MockCameraProvider`) and the Cloudflare Worker's test suite (`npm test`, vitest against a real
Miniflare-emulated Worker+D1 — pairing flow including the admin-key gate, device auth, the gallery
404 path, and the admin UIs' access control).

## Run Windows app

```
./scripts/run.ps1               # Development mode: windowed, debug-friendly
./scripts/run.ps1 -Mode Kiosk    # Kiosk mode: fullscreen
```

This only works on Windows (it launches the actual WPF process). On first run it:

- creates `data/` next to the exe (`photobooth.db`, `photos/`, `logs/`, `models/`, `cache/`) and
  runs its SQLite migrations automatically;
- scans `assets/backgrounds` / `assets/overlays` and seeds a **Demo Event** if the database is empty,
  so there's something to try immediately;
- opens the camera — a real one via `WebcamCameraProvider` if available, otherwise
  `MockCameraProvider` (a synthetic green-screen "person" test pattern), so the whole pipeline is
  still exercisable without hardware.

Admin screen: **Ctrl+Shift+A**, PIN default `1234` (change it in Admin → Obecné). Tabs: Obecné,
Kamera, Green Screen, Odečítání pozadí, AI / Hybrid, Logo / Text, Tisk, Tisk QR kódu, Cloud, Diagnostika.

**Burst capture with photo selection** (spec §57 "více fotografií"): by default each shoot takes
**3 shots** with a 1.5s pause (both configurable in Admin → Obecné, set count to 1 for the classic
single shot), showing a "Úsměv! 😊" prompt + shot counter over the live preview while shooting. The
guest then picks their favorite on a selection screen; only that one goes through processing,
saving, QR and printing — the rest of the burst is discarded. If nobody picks within the timeout,
the whole burst is discarded and the kiosk resets (nothing is saved or auto-printed for an
abandoned session).

**Physical shutter trigger** (spec §57): Admin → Obecné → "Naučit se tlačítko", then press the
remote/footswitch/clicker once — the app just remembers whichever key it happened to send (default
`Space`). Works with the overwhelming majority of photobooth remotes (Bluetooth shutter buttons, USB
footswitches, presentation clickers), since virtually all of them emulate a keyboard keypress; no
device-specific driver needed. Only acts while on the Idle or Preview screen, so it can never eat a
keystroke meant for an Admin text field.

**Branding (logo + text banner)**: Admin → Logo / Text — stamps a logo (transparent PNG
recommended) and/or a text banner (e.g. event name + date, incl. diacritics) onto the **final**
photo, on top of everything else. Banner comes in four pre-made styles — `Bar` (full-width strip),
`Pill` (rounded capsule around the text), `Ribbon` (strip with thin gold accent lines) and `Minimal`
(shadowed text, no background) — with left/center/right alignment and top/bottom or free (%)
vertical placement; the logo snaps to any corner or a free X/Y (%) position. No need to produce a
full-canvas overlay PNG for simple cases; full-canvas overlays in `assets/overlays` still work
alongside it. A broken logo path or a missing system font just skips that element — it never fails
the capture.

**QR code printer (Bluetooth/USB POS, 58mm/80mm thermal)**: Admin → Tisk QR kódu — once a guest's
photo finishes uploading and its QR download link is ready, the app can print that same QR code plus
a header/footer text (e.g. "Vaše fotografie" / "Naskenujte QR kód mobilem") on a thermal POS printer,
so the guest walks away with a paper slip instead of having to scan the on-screen QR before leaving.
This is a plain QR printout, not a payment receipt — it's **off (opt-in) by default**: the
"VYTISKNOUT QR KÓD" button on the Result screen always works once the QR is ready, and
"Automaticky vytisknout" can be turned on if every photo should print one unattended. A paired
Bluetooth POS printer shows up on Windows as a virtual COM (serial) port — pick it from the port
dropdown, no vendor SDK or driver needed. The payload is plain ESC/POS: the QR is sent as a raster
bitmap (`GS v 0`), not the printer's native 2D-barcode command, so it works even on printers whose
firmware doesn't support QR codes directly; header/footer text is transliterated to plain ASCII
(`Vaše` → `Vase`) since these printers use codepage 437, which has no Czech diacritics. Paper width is
a dot count — 384 for 58mm, 576 for 80mm. A failed print only shows an error — it never touches the
already-saved photo or blocks the next session. `Camledian.Photobooth.Printing` (portable) builds the
ESC/POS payload as a pure, unit-tested function; the actual serial transport lives in
`Camledian.Photobooth.Printing.Windows` alongside `WindowsPrintingService`, for the same
compile-anywhere/run-on-Windows split as the rest of the solution. The underlying
`ReceiptPrinterSettings`/`IReceiptPrinterService` naming is deliberately kept generic — a later
paid-photo flow (charge, then print an actual receipt) is expected to build on this same
payload/transport rather than replace it.

## Run Cloudflare backend

```
./scripts/dev-cloud.ps1
```

Applies local D1 migrations and starts `wrangler dev` on `http://localhost:8787`:

- `/` — landing page (edit pricing/copy in `cloud/src/routes/landing.ts`)
- `/api/photobooth/*` — the device API (pairing, config, events, photos, heartbeat)
- `/foto/:token` — the public QR download page (never lists photos by event — see below)
- `/admin/login` — standalone admin login (own accounts + session cookies, not tied to Camledian's
  existing shop/POS/invoicing admin — see below). `/admin/pair`, `/admin/stats`, `/admin/gallery`,
  `/admin/users` all require being logged in here first.
- First-time setup: create the first account with
  `curl -X POST "http://localhost:8787/admin/setup" -H "x-admin-key: $ADMIN_API_KEY"
  -H 'content-type: application/json' -d '{"username":"...","password":"..."}'` (password ≥ 8 chars),
  using the `ADMIN_API_KEY` from `cloud/.dev.vars`. The key goes in the `X-Admin-Key` header — it is
  deliberately not accepted as a `?key=` query parameter, which would land it in access logs and
  browser history. Refuses once any admin exists — add more accounts from `/admin/users` after
  logging in.

**Privacy note:** the public gallery only ever resolves one photo at a time by its own long random
token — there is no public listing or grouping of photos by event/location. `/admin/stats` and
`/admin/gallery` are separate, login-gated pages (counts per event, and a thumbnail lookup grid
respectively) for internal staff use only.

**Why not just reuse Camledian's existing admin?** That system covers shop administration,
invoicing, and POS sales — unrelated to photobooth device pairing. Rather than a half-integration,
the photobooth backend has its own standalone login (hashed passwords, session cookies in D1,
gated by `lib/auth.ts#requireAdminSession`). `ADMIN_API_KEY` still exists, scoped down to
server-to-server/bootstrap use (`/admin/setup`, `POST /api/photobooth/pair/confirm`) — a natural
seam for a *future* integration (e.g. invoicing or stock-material deduction per printed photo),
without forcing that decision now.

To deploy for real: `wrangler d1 create camledian-photobooth`, `wrangler r2 bucket create
camledian-photobooth-assets`, fill in the resulting IDs in `cloud/wrangler.toml`, set the secrets
from `cloud/.dev.vars.example` via `wrangler secret put <NAME>`, then `npm run deploy` inside `cloud/`.
Bind it to its own subdomain (e.g. `fotokoutek.camledian.art`) rather than a path under an existing
site's domain, to avoid colliding with that domain's own Worker routes.

## AI models

The AI/Hybrid background-removal modes need segmentation models that aren't committed to git
(spec: don't put large model binaries in the repo). Fetch them with:

```
./scripts/download-models.ps1
```

This downloads **two** models, matching the app's preview-vs-final quality split (spec §24/§25 —
preview can't wait, final quality can afford to):

- **`u2netp.onnx`** (~4.7 MB) — small/fast, used for the live preview loop. Good enough for a person
  close to the camera; noticeably weaker than the full model on fine detail (hair strands, motion
  blur) since it's a distilled/pruned network, not a scaled-down crop of the same model.
- **`u2net.onnx`** (~176 MB) — the full U-2-Net, used once after capture for the final render. Same
  320×320 input as the "p" variant (just a deeper network), so no code changes are needed to use it —
  only the weights differ. Optional: pass `-SkipFinalModel` to the script to skip it and just reuse
  the small model for everything.

Both are Apache-2.0 (https://github.com/xuebinqin/U-2-Net); if the final model is missing,
`AiBackgroundRemovalProvider` transparently reuses the preview model instead of failing the capture.
If even the preview model is missing, `BackgroundRemovalServiceFactory` falls back to Green Screen
entirely, logging one warning (visible on the Diagnostics tab) instead of crashing.

### Background Subtraction mode

A fourth background-removal mode alongside Green Screen/AI/Hybrid: Admin → "Odečítání pozadí" →
**"Vyfotit prázdné pozadí"** captures one reference photo of the empty scene, and the app then keys
out anything in later frames that still closely matches it — no green screen needed at all, since
the booth and camera don't move during an event. Falls back to Green Screen with a notice until a
reference photo has been captured. Like any keying technique it struggles if the subject's clothing
or skin tone closely matches the background color at that spot (the same class of limitation chroma
key has with green-colored clothing) — the "Citlivost" slider trades that off against tolerating
lighting drift since the reference was taken.

**BackgroundSubtractionHybrid** mode combines it with AI (`BackgroundSubtractionAiHybridProvider`) —
the no-green-screen counterpart to Hybrid (Green Screen + AI): background subtraction contributes
crisp, precise edges, AI covers the case where the subject's color happens to match the reference
photo at that spot. Requires both a captured reference photo and the AI model; falls back to Green
Screen if either is missing.

The AI project references the plain (CPU) `Microsoft.ML.OnnxRuntime` package deliberately, since it
ships native binaries for Windows **and Linux/macOS** in one package — that's what makes AI inference
actually runnable (not just compilable) in a plain Linux devcontainer too, and what the "Test AI"
diagnostics button exercises. DirectML is attempted opportunistically at runtime on Windows and falls
back to CPU if unavailable; swap the package reference to `Microsoft.ML.OnnxRuntime.DirectML` in
`Camledian.Photobooth.AI.csproj` for real GPU acceleration (Windows-only package, so doing that would
trade away the cross-platform testability described above).

## Why the WPF app builds on non-Windows

`Directory.Build.props` sets `EnableWindowsTargeting=true` for the whole solution. This lets `dotnet
build` fully compile (including XAML) `net10.0-windows` projects — the WPF app,
`Camledian.Photobooth.Printing.Windows` — on Linux/macOS too. It does **not** let you *run* them there (no Windows Desktop runtime,
no real window, no camera/printer APIs) — that verification step is what the `windows-latest` GitHub
Actions job is for. This is also why local development in a non-Windows devcontainer is limited to
building/testing, per the project's own instructions to keep the container itself as light as
possible and let CI do full compilation.

## Troubleshooting

- **"Could not open camera at index 0"** — expected on Linux/macOS or a Windows machine with no
  webcam; the app falls back to `MockCameraProvider` automatically (toggle via
  `Camera.UseMockIfUnavailable` in Admin → Kamera).
- **AI mode does nothing / falls back to Green Screen** — run `./scripts/download-models.ps1`, then
  restart the app (or just switch modes again in Admin → AI / Hybrid).
- **Print button does nothing / errors** — check Admin → Tisk lists a real printer name; a failed
  print never deletes the photo, so retry is always safe from the Result screen.
- **Cloud sync says "Nespárováno"** — pair the device first: Admin → Cloud → "Spárovat zařízení",
  then confirm the shown code at `/admin/pair` on the backend (after logging in at `/admin/login`).
- **`wrangler dev` fails to bind D1/R2** — run `npm run db:migrate:local` inside `cloud/` first (also
  done automatically by `scripts/dev-cloud.ps1`).
