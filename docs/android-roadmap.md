# Android and local multi-device roadmap

## Product direction

TimeLens for Android and TimeLens for Windows are independent, local-first products. Each must be useful without the other, an account, a cloud backend or a subscription. An optional connection should let a user inspect both devices without sending activity to a hosted service. The public website remains the download/documentation site, not an activity database.

## Implemented in the 8.4.0 Android preview

- A Kotlin / Jetpack Compose application for Android 10+ (API 29), targeting API 36. This is a native UI, not a remote website or a Windows API wrapper.
- Today, Apps, Timeline, Focus and Settings modules. Phones get five bottom tabs; windows at least 600 dp wide get a navigation rail. System insets, scrolling, large touch controls, light/dark/system themes and native date selection are supported.
- User-granted Usage access; on-resume and explicit refresh import recent Android events. Permission revocation has a visible explanation and settings shortcut.
- Exclusive foreground intervals with midnight clipping, duplicate-event handling, activity switches, screen lock/off and device restart boundaries. App totals and timeline come from the same intervals. This measures observed foreground time, not every visible split-screen window or media playing in the background.
- A private SQLite archive with daily history, app search, editable categories, per-app and daily budgets, configurable 7/30/90/365-day retention, exclusions and deletion boundaries.
- A persistent focus deadline with 15/25/50-minute presets. This first version displays completion when opened; it does not claim background alarms or enforcement.
- JSON and CSV exports for the selected day through Android's file picker. The app requests no Internet permission and disables cloud backup/device transfer of its private data.
- Gradle wrapper with distribution checksum, JVM accounting tests, device database/privacy regression test, lint and GitHub build automation.

The downloadable APK is a **debug-signed preview**, not a production/store release. Debug signing keys can differ between local builds and CI runners, so a later preview may require uninstall/reinstall (export first). Use a durable release signing key before promising upgrade-safe distribution.

## Feature parity map

| Desktop capability | Android 8.4 preview | Next work / platform constraints |
|---|---|---|
| Foreground apps, categories, timeline | Implemented | Physical-device accuracy tests across OEMs and multi-window cases |
| Historical activity | Date navigation + local archive | Scheduled archival, weekly reports and explicit coverage indicators |
| Browser sites, titles, per-domain time | Not implemented | Separate supported browser companion or opt-in browser integration; Usage access does not expose URLs |
| Background audio, PiP and media detail | Not implemented | Investigate opt-in notification/media-session integration and permission disclosures |
| Global keystroke/click counts | Not implemented | Keep out of default Android collection; Usage access cannot supply them |
| Strict/kill/hide blocking and password protection | Not implemented | Design a separate, user-enabled Android enforcement system and evaluate store policy before choosing Accessibility/VPN/device-policy APIs |
| Focus and break reminders | Foreground timer + budget status | Background completion notification, scheduled routines and pause/resume |
| Privacy rules | Package exclusions and deletion | Category matching rules, pause intervals and optional encrypted database |
| Offline work reconciliation | Not implemented | Manual session editor, overlap checks and category tagging |
| Exports | Selected day JSON/CSV | Date ranges, encrypted backup/restore, desktop import |
| Live PC connection | Not implemented | Optional authenticated local connection, described below |
| Auto-update | Not implemented | Durable signing + versioned distribution, then Play or verified download update flow |

Android's event history is short-lived. This preview refreshes today/yesterday and can import another selected day within the recent seven-day window if the OS still has it. It cannot recover expired history and does not run a background collector. Retention controls saved TimeLens history, not how long Android retains its source events. Excluding an app deletes its saved rows; restoring tracking starts a new per-app boundary, without backfilling the excluded interval. Deleting all activity advances a durable global boundary so refresh cannot resurrect it. Exports and Android's own system history remain outside this deletion operation.

## Suggested delivery sequence

1. **Trustworthy daily use:** test on Samsung, Pixel and Xiaomi phones; add scheduled archival with WorkManager, coverage diagnostics, validated background timer notifications and tests for permission withdrawal, daylight-saving changes, reboot and split screen. Archive coverage must show missing periods rather than fill gaps.
2. **Useful insight:** weekly/monthly reports, hourly patterns, app/category trends, budget notifications, calendar heatmap, configurable dashboard cards and project/session notes. Let the user define productive categories instead of making moral judgments about apps.
3. **Local connection:** begin with user-selected export/import and per-device history. Add QR-based pairing, encrypted sessions, per-device keys, revocation and a clear connection indicator. Show phone and PC time independently; if showing a combined total, explain concurrent use and offer union time versus sum of devices.
4. **Optional focus enforcement:** build only after Android API and distribution-policy review, with clear permission descriptions, predictable expiry, an emergency exit and honest limits. Never promise unbreakable blocking.
5. **Production distribution:** accessibility/localization, backup/restore, reproducible signed release builds, upgrade/migration tests, privacy documentation and Play internal testing. Keep desktop and Android release readiness distinct even if version numbers match.

## Local connection design

Do **not** expose the existing Windows dashboard on the LAN. Its API deliberately binds to loopback and its browser-extension tokens are not mobile-device administrator credentials. Use a separate, explicitly enabled companion transport with a narrow, versioned API.

- A QR code carries an expiring pairing secret, identity fingerprint and connection address; require confirmation on both devices. Bind to a user-selected private interface only while enabled.
- Establish authenticated encryption with pinned device identity. A private Wi-Fi address alone is not authentication. Prefer a reviewed pairing protocol/library; do not invent cryptography.
- Default to read-only summaries. Mutating rules, deletion, blocking or system controls require separate scopes. Persist only protected keys and permit immediate revocation.
- Keep source IDs, device IDs, UTC interval bounds, original timezone and schema version on every record. Use stable record IDs and idempotent cursor-based import so reconnects do not double count.
- Work fully offline after pairing. No hosted rendezvous, telemetry, public port forwarding or mandatory account. Import/export remains available when Wi-Fi discovery fails.

## Reference APIs

Implementation and roadmap boundaries were checked against Android's [UsageStatsManager](https://developer.android.com/reference/android/app/usage/UsageStatsManager), [usage event definitions](https://developer.android.com/reference/android/app/usage/UsageEvents.Event), [Compose setup](https://developer.android.com/develop/ui/compose/setup) and [AGP 8.13 compatibility](https://developer.android.com/build/releases/agp-8-13-0-release-notes). These references describe platform capabilities, not a guarantee of identical behavior on every device.
