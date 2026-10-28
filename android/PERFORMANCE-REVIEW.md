# Android-only smoothness and reliability pass

## Changes

- Apps uses a keyed lazy list: only visible rows and a small prefetch window are composed. The full inventory is reachable by scrolling; the repeated “Show more” steps are removed. Each destination retains its saved state.
- App icons share a 4 MiB LRU bitmap cache, with ten-minute expiry. Color/grayscale uses the same bitmap. This bounds the cache, not total process or GPU memory.
- Installed-app discovery is reused for up to one minute; explicit refreshes invalidate it.
- Comfort allowance previews share one Android usage-event query per refresh, and website rules share one website-history read. Enforcement still gets fresh measurements and preserves the unknown-access/fail-closed behavior.
- Same-date timer ticks do not cancel an unfinished refresh. Date changes cancel old work, and cancellation is checked before publishing results. The repository closes after in-flight work releases its gate.
- Appearance-only changes no longer import activity, scan installed apps or reconfigure blocking/jobs. Tracking and protection changes retain their normal refresh behavior.
- Settings and historical dates do not trigger periodic full refreshes. Today/Apps follow the current date after midnight; Block continues refreshing allowance previews. Resuming refreshes permission state.
- Historical app/category/website calculations are remembered until their data or filters change. Exclusion sets are read once per data operation rather than copied for every interval.
- Accessibility no longer subscribes to whole-window content-change events that its handler discarded. Fixed-ID address-bar polling and window-state events remain unchanged.

## Validation

Verified on 2026-10-28: 25 JVM tests and 17 selected API 35 device tests pass. Debug/release lint reports zero errors (69 warnings, including style/dependency suggestions and existing synchronous privacy preference writes). Debug and optimized release builds succeed; the unsigned optimized APK is 2.59 MiB. Device coverage includes navigation and saved scroll/search, a synthetic 1,000-app inventory, rapid date changes, appearance-only updates, storage/migration and allowance batching. See generated reports under `app/build/reports/`.

The emulator is a dedicated `TimeLensReview35` AVD, not a personal tracking installation. Chrome/browser enforcement and OEM/physical-device battery/frame-time benchmarks are not revalidated by this pass. No FPS or battery percentage guarantee is implied.

No Windows, browser-extension or website source is modified. The pass is included in the Android 8.7.0 source update on master; see CHANGELOG.md for subsequent validation. The existing release signing configuration is absent on this machine: the release APK is unsigned; the debug APK is only a development preview and cannot update a differently signed production installation. Do not uninstall a personal installation just to try the preview; use a test device/emulator or restore the original signing setup for a production update.
