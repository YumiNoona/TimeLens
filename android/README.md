# TimeLens for Android — 8.4.0 preview

Independent phone activity tracking, with a native tabbed interface and local SQLite storage. No Windows app, server, cloud account or Internet permission is required. Optional encrypted local PC pairing is planned; see the [feature map and roadmap](../docs/android-roadmap.md).

## Build

Open this directory in Android Studio, or use JDK 17+, Android SDK platform 36 and Build Tools 35.0.0:

```powershell
$env:JAVA_HOME = 'C:\Program Files\Android\Android Studio\jbr'
$env:ANDROID_HOME = "$env:LOCALAPPDATA\Android\Sdk"
.\gradlew.bat :app:assembleDebug :app:testDebugUnitTest :app:lintDebug
```

On macOS/Linux use `./gradlew` (or `bash gradlew`). Set `sdk.dir` in an untracked `local.properties` if preferred. The APK is `app/build/outputs/apk/debug/app-debug.apk`. Install on an Android 10+ phone, open TimeLens, read the permission explanation and choose **Enable usage access**. Use other apps and return to TimeLens to refresh. The native Android Settings page controls granting and revoking this access.

For database/privacy tests on a running emulator or connected test phone:

```powershell
.\gradlew.bat :app:connectedDebugAndroidTest
```

These tests use the app's own database and preferences; use a dedicated test device/profile rather than a personal tracking installation. The CI build compiles the device tests but does not run an emulator; local device execution is recorded in the release verification document.

## Preview limits

The APK is debug signed. It is intended for evaluation, not production/store distribution, and later preview builds may use a different signing key. Export before uninstalling. No production signing key is included in this repository.

Today, app history/search, editable categories, timeline, budgets, focus deadline, exclusions, retention, themes and selected-day JSON/CSV exports are implemented. Usage access does not provide website URLs or global input/media data. This version does not enforce app blocks, sound a background timer alarm, run scheduled history imports, pair with Windows, or implement complete desktop feature parity.

Refreshes import recent OS history and archive it locally. Open the app regularly; it cannot recover event history Android already discarded. Clearing TimeLens history does not erase Android's system history or exported files, and saved deletion boundaries prevent automatic reimport of deleted activity.
