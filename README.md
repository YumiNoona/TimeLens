# TimeLens

[![.NET 9](https://img.shields.io/badge/.NET-9.0-512BD4)](https://dotnet.microsoft.com/)
[![Native AOT](https://img.shields.io/badge/Native_AOT-%E2%9C%93-00AA00)](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/)
[![Svelte 5](https://img.shields.io/badge/Svelte-5-FF3E00)](https://svelte.dev/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

A privacy-first, local-only PC activity tracker. Tracks foreground apps, browser tabs, input activity, audio output, and session state — all stored in a local SQLite database. No telemetry, no cloud, no data leaves your machine.

**~10.5 MB** single-file Native AOT tray app with a real-time Svelte dashboard.

![TimeLens Dashboard](https://via.placeholder.com/800x450/0D0F0A/C8E86A?text=TimeLens+Dashboard)

## Features

- **Foreground tracking** — logs active window (exe, title, PID) via WinEvent hook
- **Browser integration** — Chrome/Firefox extensions send URL, domain, title + audible state
- **Input monitoring** — keyboard/mouse event counts in 1-minute buckets (no content logging)
- **Audio detection** — Core Audio COM enumeration, exempts idle detection when audio plays
- **Idle detection** — GetLastInputInfo with 3-minute threshold, bypassed during audio playback
- **Session tracking** — lock/unlock, sleep/resume events
- **App categorization** — auto-classifies processes into 8 categories (Work, Development, Browsing, etc.)
- **Live status** — real-time current app, idle state, audio state via shared in-memory store
- **Calendar heatmap** — 28-day activity overview
- **Timeline** — 24-hour activity blocks with category colors
- **Daily summary** — active/idle time, focus score, vs-yesterday comparison
- **History** — browse any past day's data via date picker
- **Native AOT** — single-file ~10.5 MB exe, no runtime dependencies, ~40 MB RAM

## Architecture

```
TimeLens/
├── src/
│   ├── TimeLens.Core/          # Shared models & interfaces
│   ├── TimeLens.Api/           # Kestrel API + dashboard serving
│   │   ├── Dtos/               # BrowserEventDto, DashboardResponse, etc.
│   │   ├── Services/           # AnalyticsService (SQLite queries)
│   │   └── LiveStatusStore.cs  # Real-time state between watchers & API
│   ├── TimeLens.TrayApp/       # Win32 tray app (Native AOT)
│   │   ├── Watchers/           # WinEvent, Idle, Session, Input, Audio
│   │   ├── Services/           # EventWriter, CategoryClassifier, DB init
│   │   ├── NativeTrayIcon.cs   # Raw Win32 tray icon (no WinForms)
│   │   ├── Public/             # Icon assets
│   │   └── Program.cs          # Entry point wiring all watchers + API
│   ├── TimeLens.Dashboard/     # Svelte 5 SPA
│   │   ├── src/lib/
│   │   │   ├── components/     # NavRail, LiveChip, StatCard, Timeline,
│   │   │   │                   # TopApps, CalendarHeatmap, CategoryBreakdown,
│   │   │   │                   # HistoryView, AppsView, TimelineView,
│   │   │   │                   # RulesView, SettingsView
│   │   │   ├── stores/         # Svelte stores (activity data, live status)
│   │   │   ├── api.ts          # Fetch wrapper with mock fallback
│   │   │   ├── mock.ts         # Placeholder data for dev
│   │   │   ├── types.ts        # TypeScript interfaces
│   │   │   └── colors.ts       # Category/App color mapping
│   │   ├── public/             # favicon.ico, icon.png
│   │   └── index.html
│   └── browser-extensions/
│       ├── chrome/             # MV3 service worker
│       └── firefox/            # MV2 (also works in Zen)
├── scripts/
│   └── publish.ps1             # Build SPA → AOT publish → bundle
└── tools/
    ├── genicon/                # Icon generator (dev tooling)
    └── gen-ext-icons/          # Extension PNG generator (dev tooling)
```

## Quick Start

### Prerequisites

- Windows 10+
- .NET 9 SDK
- Node.js 18+

### Run

```powershell
# 1. Build the Svelte dashboard
cd src\TimeLens.Dashboard
npm install
npm run build

# 2. Publish the tray app (Native AOT)
cd ..
dotnet publish src\TimeLens.TrayApp -c Release -r win-x64 --self-contained true

# 3. Bundle dashboard with exe
Remove-Item -Recurse -Force src\TimeLens.TrayApp\bin\Release\net9.0\win-x64\publish\dashboard
Copy-Item -Recurse src\TimeLens.Dashboard\dist src\TimeLens.TrayApp\bin\Release\net9.0\win-x64\publish\dashboard

# 4. Run
.\src\TimeLens.TrayApp\bin\Release\net9.0\win-x64\publish\TimeLens.TrayApp.exe
```

Or use the publish script:
```powershell
.\scripts\publish.ps1
```

Open [http://127.0.0.1:47821/](http://127.0.0.1:47821/) in your browser.

### Browser Extensions

**Chrome:** `chrome://extensions` → Developer mode → Load unpacked → select `src/browser-extensions/chrome/`

**Firefox / Zen:** `about:debugging#/runtime/this-firefox` → Load Temporary Add-on → select `src/browser-extensions/firefox/manifest.json`

## API Endpoints

| Method | Path | Description |
|--------|------|-------------|
| `GET` | `/api/summary?date=YYYY-MM-DD` | Dashboard data (summary, timeline, top apps, heatmap, categories, live) |
| `POST` | `/api/browser-event` | Log a browser tab visit `{domain, url, title, browser, audible}` |
| `POST` | `/api/audible-status` | Update audible tab state `{audible, browser}` |
| `GET` | `/*` | Svelte SPA + static files |

## Database

SQLite at `%LOCALAPPDATA%\TimeLens\activity.db` (auto-created).

| Table | Description |
|-------|-------------|
| `app_events` | Foreground window entries with start/end time, exe, category, idle flag |
| `browser_events` | Browser tab visits from extensions |
| `session_events` | Lock/unlock/sleep/resume events |
| `input_activity` | 1-minute aggregate keyboard/mouse counts |
| `audio_activity` | Per-process audio detection snapshots |
| `app_categories` | Exe→category override rules |

## Tech Stack

- **Backend:** .NET 9, Native AOT, Kestrel, Microsoft.Data.Sqlite
- **Frontend:** Svelte 5, Vite, TypeScript, Tabler Icons
- **Tray Icon:** Raw Win32 P/Invoke (Shell_NotifyIconW)
- **Extensions:** Chrome MV3, Firefox MV2

## License

MIT
