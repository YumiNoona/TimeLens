<div align="center">

# TimeLens

[![.NET 9](https://img.shields.io/badge/.NET-9.0-512BD4?style=flat-square&logo=dotnet)](https://dotnet.microsoft.com/)
[![Native AOT](https://img.shields.io/badge/Native_AOT-Windows-00AA00?style=flat-square)](https://learn.microsoft.com/dotnet/core/deploying/native-aot/)
[![Svelte 5](https://img.shields.io/badge/Svelte-5-FF3E00?style=flat-square&logo=svelte)](https://svelte.dev/)
[![SQLite](https://img.shields.io/badge/SQLite-local-003B57?style=flat-square&logo=sqlite)](https://sqlite.org/)

**Private activity tracking for Windows.**

TimeLens turns foreground apps, browser activity, input, audio, idle time, and sessions into a useful local dashboard. Activity stays in a SQLite database on the user's computer—there is no account, telemetry service, or cloud activity store.

[**Download TimeLens.exe**](https://time-lens-web.vercel.app/api/download) · [**Get the Firefox extension**](https://addons.mozilla.org/en-US/firefox/addon/timelens-tracker/) · [Documentation](https://time-lens-web.vercel.app/docs)

</div>

## Production v1

- Native AOT Windows tray app with an embedded Svelte dashboard and no Electron/WebView process
- Today and historical summaries, grouped timelines, heatmaps, categories, apps, sites, input, and audio activity
- App and domain focus controls with timed targets, four enforcement modes, and optional password protection
- Persistent user-arranged dashboard cards, configurable density/motion/tracking, themes, reminders, retention, exports, and goals
- Store-managed browser extension installation from Mozilla Add-ons
- Built-in updater with startup notification, manual check, SHA-256 verification, and one-click replacement from Settings

## Install

1. [Download the latest production EXE](https://time-lens-web.vercel.app/api/download).
2. Put `TimeLens.exe` in a user-writable folder and run it.
3. Open the tray menu and choose **Open Dashboard**.
4. Choose **Install Browser Extension** to open the official [TimeLens Tracker listing](https://addons.mozilla.org/en-US/firefox/addon/timelens-tracker/).

The executable is self-contained. On first launch it extracts the native SQLite library, category data, and tray icon to `%LOCALAPPDATA%\TimeLens\runtime`. The local dashboard is available only at `http://127.0.0.1:47821`.

## Build

Prerequisites: .NET 9 SDK and Node.js 22 or newer.

```powershell
.\scripts\publish.ps1                 # dashboard + standalone root EXE
.\scripts\publish.ps1 -Launch         # build and launch
.\scripts\publish.ps1 -SkipDashboard  # reuse the current dashboard build
```

Website commands:

```powershell
npm ci --prefix web
npm run web:dev
npm run web:build
```

## Repository layout

```text
TimeLens/
├── api/                         # Vercel release metadata/download proxy
├── server/                      # server-only GitHub release integration
├── web/                         # public landing page and documentation
├── src/
│   ├── TimeLens.Core/           # models and interfaces
│   ├── TimeLens.Api/            # local Kestrel API and updater
│   ├── TimeLens.Dashboard/      # embedded Svelte dashboard
│   └── TimeLens.TrayApp/        # Win32 tray host, watchers, and services
├── scripts/                     # local publish/install helpers
├── .github/workflows/release.yml
└── vercel.json                  # builds only web/ and exposes api/
```

The published browser extension is maintained by the browser store and is intentionally not included in this repository or desktop release.

## Website and Vercel

Connect this repository to Vercel using the repository root. `vercel.json` installs and builds only `web/`, publishes `web/dist`, and retains the serverless endpoints under `api/`. Desktop source, build output, and local data are excluded by `.vercelignore`.

Set these Vercel environment variables:

| Variable | Purpose |
|---|---|
| `GITHUB_TOKEN` | Fine-grained token with read-only **Contents** access to this repository; required after the repository becomes private |
| `GITHUB_REPOSITORY` | `YumiNoona/TimeLens` |
| `GITHUB_RELEASE_ASSET` | `TimeLens.exe` |
| `GITHUB_RELEASE_MAJOR` | `1` for the production-v1 update channel |

`/api/download` authenticates server-side and redirects to GitHub's short-lived signed asset URL. The EXE bypasses Vercel's function payload limit and the GitHub token is never sent to the browser. The source repository can be private while the executable download stays public; anyone with the website download URL can still obtain the EXE by design.

`/api/latest-release` returns only sanitized version, size, checksum, publication time, and same-site download information. A valid release must be non-draft, non-prerelease, use a `v1.x.x` tag, and contain both `TimeLens.exe` and `SHA256SUMS.txt`.

## Releasing and updates

The release workflow builds the dashboard, publishes the Native AOT app, verifies matching desktop/dashboard versions, and uploads:

- `TimeLens.exe`
- `SHA256SUMS.txt`

Historical tags already use `v1.0.0`, so this production source is versioned `1.0.1`. Create the `v1.0.1` tag when the reviewed changes are ready; after GitHub publishes the release, Vercel and installed apps discover it automatically.

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
- GitHub Actions release automation
