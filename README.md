<div align="center">

# TimeLens

[![.NET 9](https://img.shields.io/badge/.NET-9.0-512BD4?style=flat-square&logo=dotnet)](https://dotnet.microsoft.com/)
[![Native AOT](https://img.shields.io/badge/Native_AOT-%E2%9C%93-00AA00?style=flat-square)](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/)
[![Svelte 5](https://img.shields.io/badge/Svelte-5-FF3E00?style=flat-square&logo=svelte)](https://svelte.dev/)
[![SQLite](https://img.shields.io/badge/SQLite-local-003B57?style=flat-square&logo=sqlite)](https://sqlite.org/)
[![Windows](https://img.shields.io/badge/Windows-10+-0078D6?style=flat-square&logo=windows)](https://www.microsoft.com/windows)

**Privacy-first, local-only PC activity tracker.**
<br>
Tracks foreground apps, browser tabs, input, audio, and sessions —
<br>
all stored in a local SQLite database. No telemetry. No cloud. No data leaves your machine.

**~10.5 MB** single-file Native AOT exe · **~40 MB** RAM · zero runtime dependencies

</div>

---

## Features

| Category | Details |
|---|---|
| **Foreground tracking** | Logs active window (exe, title, PID) via WinEvent hook |
| **Browser integration** | Chrome, Edge, Brave, Firefox, Zen extensions — tracks domains, URLs, audible tabs |
| **Input monitoring** | Keyboard & mouse event counts per app in 1-minute buckets (no keylogging) |
| **Audio detection** | Core Audio COM enumeration — bypasses idle detection during media playback |
| **Idle detection** | `GetLastInputInfo` with configurable threshold, exempted during audio |
| **Session tracking** | Lock/unlock, sleep/resume events with idle reason tagging |
| **App categorization** | 8 built-in categories (Work, Development, Browsing, etc.) + custom rules |
| **Live status** | Real-time current app, idle state, audio — visible in tray tooltip & dashboard |
| **Calendar heatmap** | 28-day activity overview with color intensity |
| **Timeline** | 24-hour activity blocks colored by category — flat & grouped modes |
| **Daily summary** | Active/idle time, focus score, keystrokes/clicks, vs-yesterday comparison |
| **History** | Browse any past day via date picker with full stats |
| **11 themes** | Acid, Terminal, Moss, Copper, Arctic, Crimson, Gold, Ember, Rose, Clay, Sunset |

---

## Quick Start

### Download (recommended)

[Download the latest release](https://github.com/YumiNoona/TimeLens/releases/latest) — extract anywhere and run `TimeLens.TrayApp.exe`. Open [http://127.0.0.1:47821/](http://127.0.0.1:47821/).

### Build from source

**Prerequisites:** .NET 9 SDK · Node.js 18+

```powershell
.\scripts\publish.ps1           # build everything + deploy to root
.\scripts\publish.ps1 -Launch   # build + launch
.\scripts\publish.ps1 -Installer # build + Inno Setup installer
.\scripts\install.ps1           # one-click install to %LOCALAPPDATA%
```

### Browser extensions

| Browser | How to install |
|---|---|
| **Chrome / Edge / Brave / Arc** | `chrome://extensions` → Developer mode → Load unpacked → select `src/browser-extensions/chrome/` |
| **Firefox / Zen** | `about:debugging#/runtime/this-firefox` → Load Temporary Add-on → select `src/browser-extensions/firefox/manifest.json` |

Or right-click the tray icon → **Install Browser Extension** for a guided setup page.

---

## Architecture

```
TimeLens/
├── src/
│   ├── TimeLens.Core/              # Shared models & interfaces
│   ├── TimeLens.Api/               # Kestrel API + embedded dashboard provider
│   │   ├── Dtos/                   # Request/response DTOs
│   │   ├── Services/               # AnalyticsService (SQLite queries)
│   │   └── EmbeddedDashboardProvider.cs
│   ├── TimeLens.TrayApp/           # Win32 tray app (Native AOT)
│   │   ├── Watchers/               # WinEvent, Idle, Session, Input, Audio
│   │   ├── Services/               # EventWriter, CategoryClassifier, DB, AutoStart
│   │   ├── NativeTrayIcon.cs       # Raw Win32 P/Invoke tray icon
│   │   └── Program.cs              # Entry point — wires watchers + API
│   ├── TimeLens.Dashboard/         # Svelte 5 SPA
│   │   └── src/lib/
│   │       ├── components/         # 14 Svelte components
│   │       ├── stores/             # Reactive data stores
│   │       └── api.ts              # API client with mock fallback
│   └── browser-extensions/
│       ├── chrome/                 # MV3 (Chrome, Edge, Brave, Arc)
│       └── firefox/                # MV2 (Firefox, Zen)
├── scripts/
│   ├── publish.ps1                 # Developer build + root deploy
│   ├── install.ps1                 # One-click install
│   └── TimeLens.iss                # Inno Setup installer
└── .github/workflows/
    └── release.yml                 # CI/CD
```

---

## API

Base URL: `http://127.0.0.1:47821`

| Method | Endpoint | Description |
|---|---|---|
| `GET` | `/api/summary?date=YYYY-MM-DD` | Full dashboard payload (summary, timeline, top apps, heatmap, categories, live, input, browser, audio) |
| `GET` | `/api/input-summary?date=YYYY-MM-DD` | Per-app keystroke & click counts |
| `GET` | `/api/audio-summary?date=YYYY-MM-DD` | Per-app audio session counts |
| `GET` | `/api/browser-summary?date=YYYY-MM-DD` | Top 20 domains by visit count |
| `GET` | `/api/settings` | Current settings |
| `POST` | `/api/settings` | Update settings `{trackAudio, trackInput, theme, autoStart, ...}` |
| `GET` | `/api/rules` | Custom categorization rules |
| `POST` | `/api/rules` | Add/update rule `{pattern, category}` |
| `DELETE` | `/api/rules/{pattern}` | Delete a rule |
| `POST` | `/api/browser-event` | Log browser tab visit `{domain, url, title, browser, audible}` |
| `POST` | `/api/audible-status` | Update audible tab state `{audible, browser}` |
| `GET` | `/api/running-processes` | User-facing processes for rule suggestions |
| `GET` | `/extension-setup` | Browser extension install guide page |
| `GET` | `/*` | Svelte SPA & static assets |

---

## Database

SQLite at `%LOCALAPPDATA%\TimeLens\activity.db` (WAL mode, auto-vacuum, 90-day retention).

| Table | Description |
|---|---|
| `app_events` | Foreground window entries — exe, title, PID, category, session state, idle reason |
| `browser_events` | Browser tab visits from extensions |
| `session_events` | Lock/unlock/sleep/resume events |
| `input_activity` | 1-minute aggregate keystroke & click counts per app |
| `audio_activity` | Per-process audio playback snapshots |
| `custom_rules` | User-defined exe → category overrides |
| `settings` | Key-value config (tracking toggles, theme, auto-start, etc.) |

---

## Tech Stack

| Layer | Technology |
|---|---|
| **Backend** | .NET 9 · Native AOT · Kestrel · Microsoft.Data.Sqlite |
| **Frontend** | Svelte 5 · Vite · TypeScript · Tabler Icons |
| **Tray icon** | Raw Win32 P/Invoke (`Shell_NotifyIconW`) |
| **Extensions** | Chrome MV3 · Firefox MV2 |
| **Installer** | Inno Setup 6 (per-user, no admin) |
| **CI/CD** | GitHub Actions (builds on tag, uploads to release) |

---

## License

[MIT](LICENSE) © TimeLens

This project is free and open source. You can use, modify, and distribute it under the terms of the MIT license. No attribution required — but appreciated.
