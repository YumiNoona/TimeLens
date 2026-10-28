# Android 8.5.0

This update focuses on the APK. Windows and public website application files are unchanged.

## Application changes

- Replaced the starter UI with Today, Apps, Reports and Focus navigation plus Timeline/Settings under More. Added native icons, an adaptive launcher icon, six accents, compact mode and system-bar contrast matching the app theme.
- Added hourly charts, weekly bars, category distribution, a calendar, 7/30/90-day reports, daily averages, user-defined focus scores, switches, longest sessions and yesterday comparisons.
- Added installed-app search/sort, automatic categories, richer details and budgets. Large app/timeline lists load progressively.
- Added timer pause/resume, custom lengths, saved deadlines and background completion notifications, with optional precise-alarm access.
- Added background archival with JobScheduler, reboot/update recovery and permission health controls.
- Added opt-in app blocking, focus-only rules, daily-limit rules, weekday/time schedules, overnight handling, custom presentation, a pause control, PIN protection and block activity history.
- Added durable collection pauses, range exports and validated Android JSON restore, including exports from the old preview.
- Fixed partial-range import double-counting and retention while the dashboard is closed. Unavailable usage permissions/source data preserve saved history.
- Added optimized, resource-shrunk release packaging with a persistent local signing identity. APK size is approximately 2.7 MB, compared with roughly 60 MB for the original debug package.

## Verification

- Twenty JVM tests cover accounting, pauses, PIN hashing, block modes, budget boundaries, weekday/overnight schedules and daylight-saving aggregation.
- Dedicated-emulator tests cover the 8.4 database upgrade, partial/idempotent imports, JSON validation/restore, deletion/privacy boundaries, permission withdrawal, actual Chrome blocking/relaunch/pause, scheduled notifications and background archival.
- Debug and optimized release builds pass. Android lint has zero errors; dependency-update/style suggestions remain.
- The signed, optimized APK was installed on an API 35 emulator. Its real Accessibility overlay over Chrome was verified independently of the debug tests; phone screens were reviewed visually.
- Repository checks support independent Android versioning. No desktop/web application source files were changed.

## Platform boundaries

Blocking covers selected app windows through the optional Accessibility service. It cannot kill arbitrary Android processes or prevent system permission changes. Settings, the launcher and dialer are protected from rules. Daily-budget enforcement depends on Android's usage events and may lag.

Background archival and alerts depend on permissions, Doze and OEM battery policies. Prompt timer delivery requires the optional alarm grant. Empty historical days mean no saved activity, rather than proof that the phone was unused.

No Internet permission is requested. Browser URL/title tracking, background media details, global input counts and local PC pairing remain separate future integrations.
