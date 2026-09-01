<div align="center">

# TimeLens

[![.NET 9](https://img.shields.io/badge/.NET-9.0-512BD4?style=flat-square&logo=dotnet)](https://dotnet.microsoft.com/)
[![Native AOT](https://img.shields.io/badge/Native_AOT-Windows-00AA00?style=flat-square)](https://learn.microsoft.com/dotnet/core/deploying/native-aot/)
[![Svelte 5](https://img.shields.io/badge/Svelte-5-FF3E00?style=flat-square&logo=svelte)](https://svelte.dev/)
[![SQLite](https://img.shields.io/badge/SQLite-local-003B57?style=flat-square&logo=sqlite)](https://sqlite.org/)

**Private activity tracking for Windows.**

TimeLens turns foreground apps, browser activity, input, audio, idle time, and sessions into a useful local dashboard. Activity stays in a SQLite database on the user's computer—there is no account, telemetry service, or cloud activity store.

[**Download TimeLens Setup**](https://timelens.venusapp.in/api/download) · [**Get the Firefox extension**](https://addons.mozilla.org/en-US/firefox/addon/timelens-tracker/) · [Documentation](https://timelens.venusapp.in/docs)

</div>

## Production v5

- Native AOT Windows tray app with an embedded Svelte dashboard and no Electron/WebView process
- Today and historical summaries, grouped timelines, heatmaps, categories, apps, sites, input, and audio activity
- App and domain focus controls with timed targets, target-aware enforcement modes, custom media reminders, and optional password protection
- User-arranged cards on Today and History, plus configurable density/motion/tracking, themes, reminders, retention, exports, and goals
- Store-managed browser extension installation from Mozilla Add-ons
- Per-user Windows installer with install-location, startup, desktop-shortcut, and launch options plus a clean uninstaller
- Built-in updater with startup notification, manual check, SHA-256 verification, and one-click replacement from Settings

## Install

1. [Download the latest TimeLens installer](https://timelens.venusapp.in/api/download).
2. Choose an installation folder and whether TimeLens should start with Windows or create a desktop shortcut.
3. Finish setup and open the tray menu to choose **Open Dashboard**.
4. Choose **Install Browser Extension** to open the official [TimeLens Tracker listing](https://addons.mozilla.org/en-US/firefox/addon/timelens-tracker/).

Setup installs the self-contained app to `%LOCALAPPDATA%\Programs\TimeLens` by default, without requesting administrator access. On first launch TimeLens extracts the native SQLite library, category data, and tray icon to `%LOCALAPPDATA%\TimeLens\runtime`. The local dashboard is available only at `http://127.0.0.1:47821`, and uninstalling the app does not silently erase activity history.

## Build

Prerequisites: .NET 9 SDK, Node.js 22 or newer, Inno Setup 6, and Visual Studio C++ build tools with a Windows SDK for Native AOT.

```powershell
.\scripts\publish.ps1                 # dashboard + app EXE + Windows installer
.\scripts\publish.ps1 -Launch         # build and launch
.\scripts\publish.ps1 -SkipDashboard  # reuse the current dashboard build
.\scripts\publish.ps1 -SkipInstaller  # build only the standalone app EXE
```

Website commands:

```powershell
npm ci --prefix web
npm run web:dev
npm run web:build
```

### Startup regression checks

Version 5.0.1 applies saved website-notification corner and media-layout changes immediately to an open blocked tab. The extension hydrates custom media on its live block check, moves the existing reminder to the selected corner, and redraws it as Thumbnail, Large, or Full banner without waiting for a page reload or the next reminder interval. The desktop dashboard now keeps the app picker from resizing its card and records block-opening counts in Block and History.

```powershell
dotnet run --project tests/TimeLens.Startup.Tests -c Release
dotnet run --project tests/TimeLens.Startup.Tests -c Release -- --tray
.\scripts\test-startup.ps1 -ExePath .\TimeLens.exe
.\scripts\test-block-modes.ps1 -ExePath .\TimeLens.exe
```

The first command tests fresh and failed-first-launch databases, saved retention, legacy rules, corrupt data, and per-user Windows startup registration. It also runs in the publish script and release workflow. `--tray` additionally tests native shell registration, keyboard activation, and icon restoration on an interactive Windows desktop. The block-mode script launches an isolated copy plus an off-screen Win32 probe and verifies that Hide minimizes it, Kill terminates it, and Strict terminates both immediately and after relaunch. Website contract tests cover Notify and Strict separately.

Close any running TimeLens instance before the packaged startup test. This test launches the exact EXE from an unrelated working directory, checks the native tray and embedded dashboard/API, then closes it. Its `--smoke-test` launch mode uses a new data directory under `artifacts/`, skips onboarding and update checks, and does not change the normal activity database or startup registration. Logs remain in the isolated directory for diagnosis. This local test does not replace testing the installer on a clean Windows VM.

## Repository layout

```text
TimeLens/
├── api/                         # Vercel release metadata/download proxy
├── server/                      # server-only GitHub release integration
├── web/                         # public landing page and documentation
├── installer/                   # Inno Setup per-user Windows installer
├── src/
│   ├── TimeLens.Core/           # models and interfaces
│   ├── TimeLens.Api/            # local Kestrel API and updater
│   ├── TimeLens.Dashboard/      # embedded Svelte dashboard
│   ├── TimeLens.TrayApp/        # Win32 tray host, watchers, and services
│   └── browser-extensions/      # Chrome and Firefox companion sources
├── scripts/                     # local publish/install helpers
├── .github/workflows/release.yml
└── vercel.json                  # builds only web/ and exposes api/
```

The release includes Chrome and Firefox extension packages alongside the desktop binaries. The Firefox Add-ons listing remains the recommended signed installation; release packages are useful for review and developer installation.

## Website and Vercel

Connect this repository to Vercel using the repository root. `vercel.json` enters `web/` explicitly for dependency installation and the build, publishes `web/dist`, and retains the serverless endpoints under `api/`. Desktop source, build output, and local data are excluded by `.vercelignore`.

Set these Vercel environment variables:

| Variable | Purpose |
|---|---|
| `GITHUB_TOKEN` | Optional for a public repository; required for a private repository and recommended for steadier API rate limits. Use a fine-grained token with read-only **Contents** access only to this repository |
| `GITHUB_REPOSITORY` | `YumiNoona/TimeLens` |
| `GITHUB_RELEASE_ASSET` | `TimeLens.exe` |
| `GITHUB_DOWNLOAD_ASSET` | `TimeLens-Setup.exe` |
| `GITHUB_RELEASE_MAJOR` | `5` for the production-v5 update channel; stale lower values are safely raised to the current production minimum |

All non-secret values above already have these defaults in the server code. With a public repository the download works without configuring any variables. Add `GITHUB_TOKEN` if the repository becomes private or if anonymous GitHub API rate limits are too low for the site traffic.

`/api/download` redirects website visitors to the installer, while `/api/app-download` is reserved for the verified raw-EXE updater flow. Both authenticate server-side and redirect to GitHub's short-lived signed asset URLs. The binaries bypass Vercel's function payload limit and the GitHub token is never sent to the browser. The source repository can be private while downloads remain available through the website by design.

`/api/latest-release` returns only sanitized version, size, checksum, publication time, and the raw-app update URL. A valid release must be non-draft, non-prerelease, use a `v5.x.x` tag, and contain `TimeLens.exe`, `TimeLens-Setup.exe`, and `SHA256SUMS.txt`.

## Releasing and updates

The release workflow builds the dashboard, publishes the Native AOT app, verifies matching desktop/dashboard versions, and uploads:

- `TimeLens.exe`
- `TimeLens-Setup.exe`
- `TimeLens-Chrome-Extension.zip`
- `TimeLens-Firefox-Extension.zip`
- `SHA256SUMS.txt`

The production desktop, dashboard, installer, and companion extension packages are released as `v5.0.1`. Vercel serves the guided installer to website visitors, while installed apps discover the separately checksummed desktop executable through the update feed.

The desktop updater downloads only over HTTPS, limits the payload size, checks the PE signature and exact file length, verifies SHA-256 against the release manifest, and then uses a hidden replacement helper to restart the app. It refuses to run from `dotnet` development hosts or from an unwritable install folder.

## Local API

Base URL: `http://127.0.0.1:47821`

| Method | Endpoint | Description |
|---|---|---|
| `GET` | `/api/summary?date=YYYY-MM-DD` | Daily dashboard summary and timeline data |
| `GET` | `/api/settings` | Current local preferences |
| `POST` | `/api/settings` | Save local preferences |
| `GET` | `/api/rules` | Categorization rules |
| `POST` | `/api/rules` | Add or update a rule |
| `DELETE` | `/api/rules/{pattern}` | Delete a rule |
| `POST` | `/api/browser-event` | Receive an event from the installed extension |
| `GET` | `/api/browser-block-state?domain=…` | Return the current browser action and custom presentation |
| `GET` | `/api/extension-status` | Extension connection state |
| `GET` | `/api/update/status` | Check the production update feed |
| `POST` | `/api/update/install` | Verify, stage, and restart into an update |
| `GET` | `/extension-setup` | Compatibility redirect to the official extension listing |
| `GET` | `/*` | Embedded dashboard SPA and static assets |

## Privacy and data

The activity database is `%LOCALAPPDATA%\TimeLens\activity.db` and uses SQLite WAL mode. TimeLens records aggregate input counts, not typed characters. Browser data is accepted from the installed extension over the local API. Settings, categories, rules, block logs, idle spans, and activity history remain on the device unless the user explicitly exports them.

## Stack

- .NET 9, Native AOT, Kestrel, Microsoft.Data.Sqlite
- Svelte 5, Vite, TypeScript, local fonts and icons
- Raw Win32 tray integration and Windows activity/audio/session APIs
- Vercel static hosting and serverless release proxy
- Inno Setup per-user installer
- GitHub Actions release automation

<p align="center">Built With 💙 Made By <a href="https://venusapp.in/">Veil</a></p>
