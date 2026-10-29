# 5.0.0 — Simpler mobile history

- Removed the activity calendar grid and its caption from Android History. Grouped app history follows the period summaries directly, without a long grid of empty cells.
- Date selection, range summaries, weekly chart, saved activity and expandable app visits remain available.
- Version 5.0.0 (build 80705) uses the existing production signing identity for an in-place update from the signed 4.0.0 builds.

# 4.0.0 — Faster tabs, favorites, bulk rules and removal controls

## UI refinements · build 80704

- Removed duplicate theme selectors, the Independent/Private/Local card and Today's customize button. Dashboard modules now have their own Settings section.
- Replaced Block's tab strip and switch tracks with labelled navigation cards, power actions and compact rule controls.
- Fixed the year-range layout with equal-width, adaptive choices. App history defaults to collapsed per-app totals with expandable visits, search, sorting, category filters and Collapse all. Period totals are separately collapsible.
- See [UI refinement notes](UI-REFINEMENTS-4.0.0.md) for the build identifier and verification.

## Initial release

- Virtualized History and Block, including calendar rows and expanded timeline groups. Cached reports load independently of daily activity, and fresh tab visits no longer force complete refreshes.
- Restricted theme frame capture to explicit appearance changes. Prepared installed-app metadata off the UI thread and reused cached icons immediately.
- Added app favorites and a favorites filter, plus bulk Comfort/Strict rules with favorites/distraction presets.
- Added Protection & removal: standard Android administrator confirmation for personal phones and Android-enforced uninstall policy for authorized managed-device owners. No automatic enrollment, Settings interception, password or device-wipe policies.
- Verified 32 JVM tests and 36 selected API 35 tests, including actual uninstall rejection and recovery under an opt-in temporary managed role. Release/preview build and lint passed. Twelve repository tests passed. The unchanged Brave-specific check was not rerun in this emulator; other adapters/OEM devices remain separate validation.
- Production signing uses the original release certificate. The missing temporary 3.0-preview key prevents an in-place update from that preview; see the migration notes.
- See [release notes](RELEASE-4.0.0.md) for reproducible performance observations, management/recovery boundaries and test scope.

# 8.7.0 — Appearance, browser adapters and stronger enforcement

- Replaced selected-navigation bubbles with filled/outlined icon pairs. The top-right action is now a sun/moon appearance toggle; manual archival remains in Settings.
- Fixed the actual Compose state invalidation behind unreliable appearance changes. Added System/Light/Dark policy, persistent overrides, an animated palette that retains screen state, and matching system-aware blocking banners.
- Added explicit Brave, Vivaldi, DuckDuckGo and additional Chrome/Firefox/Edge channel adapters. Unknown packages still cannot read arbitrary UI text; only known address bars are inspected.
- All matching domain allowances apply, preventing a generous child rule from bypassing an exhausted parent. Strict still ignores the legacy global pause. Matched protected apps stay covered during temporary measurement failures.
- Screen-off stops browser/recheck callbacks and clears overlays; unlock resumes enforcement. Safe system packages remain accessible. This is not device-owner lockdown.
- Includes the Android-only smoothness pass: lazy app lists, a bounded icon cache, batched allowance queries, less repetitive history work and safer refresh cancellation.
- Verified 30 JVM tests, 18 selected API 35 tests, plus a real Brave Strict/screen-off/unlock/launcher test. Debug/release builds and lint pass with zero errors. The optimized unsigned APK is approximately 2.61 MiB. New browser address adapters still need device/version testing; Brave app-blocking coverage does not prove every address-bar version works.
- Signing identity is pending because the original keystore is unavailable. No replacement key is silently generated, and the older signed release is not overwritten.

# 8.6.0 — Navigation, monochrome and blocking

- Fixed shared scrolling and unstable composition caused by inserting a loading row during preference refreshes. Each destination now owns saved scroll and control state, and refresh uses an overlay indicator.
- Added icon navigation, a Settings destination, expandable settings, compact search/filter controls, bundled Space Grotesk and monochrome dark/light themes with optional grayscale app icons.
- Reports is now History with calendar, historical trends, app visits and observed websites. Focus is now Block; timers have been removed from the UI.
- Added Comfort rolling 24-hour allowances and Strict rules for apps and domains, a custom banner preview and block activity. New modes ignore the old global pause. Safe system apps remain accessible.
- Added opt-in fixed-ID browser address-bar integration. Only hostnames and observed intervals are saved locally; URL paths, queries, credentials and webpage content are not stored. Website support is limited by exposed browser controls, including private-tab visibility.
- App Comfort uses Android events independently of archive deletion/exclusions. Blocking-overlay intervals are excluded from those allowances. Website observations have retention and separate deletion controls.
- Migrates Always and daily-budget rules; disables legacy timer/schedule rules and rules with an inactive daily budget for review. Existing 8.5 release installs update with the same signing identity.
- App JSON/CSV export still covers app history; website export/restore and PC pairing remain future work. Website integration has Chrome emulator coverage; other browser adapters and real OEM devices remain beta validation work.
- Verified 25 JVM tests and 19 API 35 device tests, including saved navigation/search/scroll, toggles and refresh, actual Chrome host observation, both website modes and isolated upgrade migrations. Release build and lint passed (0 errors); 10 repository tests passed. The optimized APK uses the existing release signing identity.

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
