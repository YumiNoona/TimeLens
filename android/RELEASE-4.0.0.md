# Android 4.0.0 — Faster navigation and protection controls

This update preserves the 3.0 appearance, original icon colors, circular theme reveal and one-year history. Windows, the website and browser companions are unchanged.

## Navigation

- History and Block now use keyed lazy lists. Off-screen calendar rows, visits, expanded groups, rules and block events are not composed together.
- Returning to a fresh date or report reuses it. A history range change loads the report separately from app discovery, usage archival and allowance queries.
- The circular theme reveal captures one frame only when requested. Ordinary navigation draws directly, without recording a full-screen graphics layer each time.
- Installed-app categories/goals are prepared off the main thread. App rows reuse cached icons immediately and read shared rule/favorite snapshots.
- Saved search, date, range, scroll, filters and appearance remain separate for each destination.

The repeatable API 35 emulator workload uses 600 selected-day visits, 40 app summaries and a populated year. Three returns to History measured 874/827/735 ms before and 162/155/121 ms after the changes. Window frame-duration p95 in those runs dropped from 578 ms to 112 ms. These are debug-emulator observations, including test synchronization, not a physical-device FPS or battery guarantee.

## New controls

- Star/unstar apps and filter favorites without starting an activity scan.
- Create Comfort or Strict rules for several apps together. Favorites and distraction-category presets help select apps; allowances apply separately to each app. Existing rules for chosen apps are replaced only when saved.
- **Settings → Protection & removal** reports the blocking service and PIN state and offers Android's explicit administration flow.

## Removal protection

On a normal phone, the optional administrator adds an extra deactivation/confirmation step. Android still lets the owner deactivate it and uninstall. It requests no password, screen-lock or device-wipe policies, and does not intercept uninstall, Settings or Accessibility screens.

If TimeLens is independently provisioned as an authorized device/profile owner, the panel can apply Android's `setUninstallBlocked` policy for its own package and release that policy. Releasing the policy does not remove device/profile ownership; the provisioning administrator may still need to release the device for uninstall. Enrollment and device resets are never performed by the app.

The managed policy test is restricted to an owner-free emulator, a `testOnly` debug package, and the explicit `allowManagedPolicyTest=true` instrumentation argument. It enables the policy, verifies Android rejects an uninstall attempt, releases it and removes its temporary owner role in cleanup. Production and optimized-preview packages are not test-only.

References: [Android device policy](https://developer.android.com/reference/android/app/admin/DevicePolicyManager#setUninstallBlocked(android.content.ComponentName,%20java.lang.String,%20boolean)), [administration overview](https://developer.android.com/work/device-admin).

## Installation and verification

The published 4.0.0 APK uses the original TimeLens production signing certificate. Existing 8.5/8.6 production-signed installations can update with that identity. The 3.0 testing preview uses a different, temporary certificate; its private key is unavailable, so Android cannot update that installation in place.

For a deliberate move from the 3.0 preview, export wanted app activity as JSON before uninstalling and restore it after installing 4.0.0. These exports do not automatically retain website observations, rules, categories or PIN settings. Test on another device first if those local settings/history must remain untouched. No existing personal installation is uninstalled by this work.

Verified: 32 JVM tests, 36 selected API 35 device tests and 12 repository tests. Device coverage includes large-history virtualization, saved navigation, theme capture, favorites, bulk rules, Chrome enforcement, permission withdrawal, storage/migration and actual managed uninstall rejection/recovery. The unchanged Brave-specific integration check was not rerun on this emulator. Release/optimized-preview builds and lint pass with zero errors; other browser versions and OEM devices still require separate validation.
