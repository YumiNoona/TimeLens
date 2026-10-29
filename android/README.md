# TimeLens for Android

Native local activity tracking for Android 10+, with Today, Apps, History, Block and Settings. Version 1.0.0 uses build code 80707 and the persistent production signing identity. No account, cloud backend or Internet permission is required.

## Features

- Daily summaries, hourly activity, categories, goals and favorites.
- Search/filter/sort for apps and selected-day history. One total per app with expandable visits; range reports include a year. No activity-calendar grid.
- Comfort allowances over a rolling 24 hours and Strict app/site rules, bulk app rules, optional PIN and custom blocking banners.
- Optional website observations through known address bars in supported browsers, including Chrome, Firefox, Edge, Brave, Samsung Internet, Vivaldi and DuckDuckGo.
- Light/dark toggle, original or grayscale app icons, compact cards, clock/duration choices and configurable dashboard modules.
- Native switches, full-row settings taps, clear search controls, numeric/URL keyboards, Search/Done actions and keyboard-aware layout.
- App-activity CSV/JSON export and supported JSON restore, exclusions, retention and local deletion controls.

## Install

Use the signed APK from the unified GitHub release. The signed AAB is for Play Console upload and is not directly installable. Usage access enables app reports and Comfort measurement. Blocking additionally needs the optional Accessibility service after its disclosure; sideloaded APKs may require Android App info → Allow restricted settings. Website address-bar access is a separate opt-in in Block → Web.

Website time overlaps browser app time. Hidden bars, unsupported versions and OEM restrictions can leave gaps; private tabs can be observed if their browser exposes an address bar. The app reads known address controls, not passwords, page contents, URL paths or queries. Android does not collect global input counts or offer Windows media detail, and it does not pair/sync with the PC app.

Standard personal-phone removal protection adds an administrator confirmation step. Android-enforced uninstall blocking requires an independently provisioned, authorized device/profile owner. The app does not enroll a phone automatically.

## Build and verify

Use JDK 17+, SDK platform 36 and Build Tools 35.0.0. From this directory:

```powershell
.\gradlew.bat :app:testDebugUnitTest :app:lintRelease :app:assembleRelease :app:bundleRelease
```

Set the untracked signing.properties fields storeFile, storePassword, keyAlias and keyPassword for a signed release. Keep the keystore and credentials outside Git. APKs are optimized and non-debuggable; the AAB is signed with the same identity. Without local signing, CI builds unsigned validation artifacts and does not publish them as installable releases.

Run :app:connectedDebugAndroidTest only on a dedicated test emulator: tests modify their test installation's data and permissions. App-activity exports are not complete backups of every preference, rule, website observation or PIN. A signed update preserves existing data; an older preview signed with a different key cannot update in place.
