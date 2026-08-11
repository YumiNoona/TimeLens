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

**~18 MB** standalone Native AOT executable · zero runtime dependencies · copy and run

</div>

---

## Features

| Category | Details |
|---|---|
| **Foreground tracking** | Logs active window (exe, title, PID) via WinEvent hook |
| **Browser integration** | Chrome, Edge, Brave, Firefox, Zen extensions — tracks domains, URLs, audible tabs |
| **Focus Mode** | Blocks exact app and domain matches with notify, minimize, terminate, and strict actions |
| **Input monitoring** | Keyboard & mouse event counts per app in 1-minute buckets (no keylogging) |
| **Audio detection** | Core Audio COM enumeration — bypasses idle detection during media playback |
| **Idle detection** | `GetLastInputInfo` with configurable threshold, exempted during audio |
| **Session tracking** | Lock/unlock, sleep/resume events with idle reason tagging |
| **App categorization** | 8 built-in categories (Work, Development, Browsing, etc.) + custom rules |
| **Live status** | Real-time current app, idle state, and audio context in the tray tooltip |
| **Calendar heatmap** | GitHub-style activity overview with configurable 4-week, 3-month, or 6-month range |
| **Timeline** | Meaningful activity blocks grouped by default, with expandable app detail and a configurable noise threshold |
| **Daily summary** | Active/idle time, focus score, keystrokes/clicks, vs-yesterday comparison |
| **History** | Browse past days with a date picker, heatmap, daily summary, apps, sites, categories, and timeline |
| **Preferences** | Default tab, interface density, motion, refresh rate, time format, tracking signals, reminders, retention, exports, and goals |
| **11 themes** | Acid, Terminal, Moss, Copper, Arctic, Crimson, Gold, Ember, Rose, Clay, Sunset |

---

## Quick Start

### Download (recommended)

[Download the latest release](https://github.com/YumiNoona/TimeLens/releases/latest), place `TimeLens.exe` anywhere, and run it. TimeLens starts in the system tray and serves the dashboard at [http://127.0.0.1:47821/](http://127.0.0.1:47821/).

The executable is self-contained. On first launch it extracts only the native SQLite runtime and built-in category/icon resources to `%LOCALAPPDATA%\TimeLens\runtime`. Activity data remains in `%LOCALAPPDATA%\TimeLens`.

### Build from source

**Prerequisites:** .NET 9 SDK · Node.js 18+

```powershell
.\scripts\publish.ps1                 # build dashboard + standalone root EXE
.\scripts\publish.ps1 -Launch         # build and launch TimeLens.exe
.\scripts\publish.ps1 -SkipDashboard  # reuse an existing dashboard dist build
.\scripts\install.ps1                 # build and install to %LOCALAPPDATA%\TimeLens
```

`publish.ps1` produces a single copy-and-run `TimeLens.exe` in the repository root. Dashboard assets, browser-extension packages, SQLite, categories, and the tray icon are embedded in the executable.

### Browser extensions

| Browser | How to install |
|---|---|
| **Chrome / Edge / Brave / Arc / Opera / Vivaldi** | Download the Chromium ZIP → extract it → open the browser extensions page → enable Developer mode → Load unpacked |
| **Firefox / Zen** | Download the Firefox ZIP → extract it → open `about:debugging` → Load Temporary Add-on → select `manifest.json` |

With TimeLens running, open [http://127.0.0.1:47821/extension-setup](http://127.0.0.1:47821/extension-setup) or right-click the tray icon and choose **Install Browser Extension**. The setup page downloads both ZIPs directly from the running EXE and shows the installed extension's connection status.

For extension development, the unpacked sources remain under `src/browser-extensions/chrome` and `src/browser-extensions/firefox`. `src/browser-extensions/shared/background.js` is the shared source used by both builds.

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
│   │       ├── components/         # Dashboard views, including History and Focus Mode
│   │       ├── stores/             # Reactive data stores
│   │       └── api.ts              # Typed local API client
│   └── browser-extensions/
│       ├── chrome/                 # MV3 (Chrome, Edge, Brave, Arc)
│       ├── firefox/                # MV2 (Firefox, Zen)
│       └── shared/                 # Shared tracking/blocking source
├── scripts/
│   ├── publish.ps1                 # Developer build + root deploy
│   └── install.ps1                 # Source build + per-user install
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
| `GET` | `/api/block/stats` | Block enforcement counts for the current day |
| `POST` | `/api/block/enforce` | Enforce an active executable blocklist entry |
| `GET` | `/api/extension-status` | Current extension connection, browser, and version |
| `GET` | `/extension-setup` | Browser extension install guide page |
| `GET` | `/extension/download/{chromium\|firefox}` | Download an extension ZIP embedded in the EXE |
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
| `block_log` | Successful Focus Mode app enforcement events |
| `idle_spans` | Recorded idle periods and reasons |

---

## Tech Stack

| Layer | Technology |
|---|---|
| **Backend** | .NET 9 · Native AOT · Kestrel · Microsoft.Data.Sqlite |
| **Frontend** | Svelte 5 · Vite · TypeScript · local fonts · Lucide + Tabler icons · Morphicons transitions |
| **Tray icon** | Raw Win32 P/Invoke (`Shell_NotifyIconW`) |
| **Extensions** | Chrome MV3 · Firefox MV2 |
| **Packaging** | Single self-contained Windows executable with embedded dashboard and extensions |
| **CI/CD** | GitHub Actions release workflow |

---

## License

[MIT](LICENSE) © TimeLens

This project is free and open source. You can use, modify, and distribute it under the terms of the MIT license. No attribution required — but appreciated.
