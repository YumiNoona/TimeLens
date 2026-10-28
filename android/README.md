# TimeLens for Android — 8.6.0

Independent local activity tracking for Android 10+, with a native dashboard, reports, app blocking and personal settings. Android now versions independently of the Windows/web app. No account, analytics SDK, cloud backend or Internet permission is required.

## Available features

- Five independent destinations: Today, Apps, History, Block and Settings. Phone navigation uses accessible icons; larger screens use a rail. Each page keeps its own scroll and form state. Refresh indicators no longer change the content position.
- Space Grotesk bundled offline, with a complete type scale. Black-and-white dark/light themes; real app icons with grayscale on/off. Refresh is the only top-right action.
- Today contains current-day screen time, goals, productive-category score, switches, longest session, yesterday comparison, hourly activity and a short top-app preview.
- Apps contains the app inventory, details, categories and optional daily comparison goals. Search opens from its icon. Filters open a dedicated dialog for used/installed, category and sorting.
- History contains 7/30/90-day totals, averages, weekly patterns, categories, a calendar, selected-day app visits and observed website domains.
- Block contains app rules, website rules, a custom banner preview and activity. **Comfort** permits an allowance in a rolling 24-hour window. **Strict** blocks access while the rule and blocking are enabled. Neither has an overlay pause shortcut. Optional PIN protects changes.
- App Comfort measurement reads Android usage directly, independent of history exclusion, collection pause and archive deletion. Time under TimeLens's blocking overlay is subtracted while the service is running.
- Optional, separately disclosed browser integration recognizes known address-bar controls for Chrome, Firefox, Edge and Samsung Internet. It saves canonical hostnames and observed foreground time only. Website rules include subdomains. A Strict parent rule takes precedence over a Comfort child rule.
- Background archival, retention, privacy exclusions, collection pauses, app-activity JSON/CSV export and validated JSON restore. Website observations are stored separately and are not yet part of these export files. Website history can be deleted separately or with all saved activity.
- Expandable Settings modules: permissions, appearance, tracking/storage, goals/categories, privacy, blocking PIN and app-activity export.

## Install and permissions

Install the signed `TimeLens-Android-8.6.0.apk`. An existing release-signed 8.5.0 installation can be updated directly without uninstalling.

1. Enable Usage access to read local foreground app activity and measure app Comfort allowances.
2. Open **Block → Set up blocking**, read the disclosure, then enable **TimeLens app blocking** in Android Accessibility settings. Downloaded APKs may require **App info → menu → Allow restricted settings** first.
3. Add an app or domain rule. Enable blocking. Choose Comfort minutes or Strict. Set an optional PIN under **Settings → Blocking PIN**.
4. For websites, separately enable **Block → Website rules → Website tracking** after reading the address-bar disclosure.

The app retains **no Internet permission**, account or cloud backend. Browser access is disabled by default. When enabled, the service only requests known address-bar controls by their fixed IDs; it does not traverse webpage content, read passwords or save URL paths/query strings. Private tabs can be included if a browser exposes its address bar. Hidden bars and unsupported browser versions leave gaps and can bypass website rules. Website time overlaps browser app time and must not be added to app totals.

Android Settings, the launcher, dialer and TimeLens remain accessible. Strict removes the in-banner pause escape; it is **not device-owner lockdown**. The phone owner can still edit rules, disable Accessibility or uninstall the app. App Comfort requires usage access; the service covers the selected app if that access is unavailable rather than inventing an allowance. OEM service restrictions can affect enforcement.

On upgrade, old Always rules become Strict and old daily-budget rules become rolling Comfort rules. Former focus/schedule rules are disabled for review so the upgrade does not unexpectedly make them permanent blocks. Existing timer state is cleared; timers are no longer part of the UI.

### Moving from the 8.4 debug preview

The old APK used debug signing. Android cannot update it with a different release certificate. Export wanted days as JSON from the old app **before uninstalling it**, install this signed APK and use **Settings → Restore Android JSON export**. Restore preserves unrelated local activity and validates overlaps. Exclusions and retention still apply; choose a longer retention period before restoring older history. Activity exports do not include all old UI preferences.

Future APKs signed with the same persistent release key can update this build normally. Keep the local signing identity and password backed up securely; neither is committed to Git or embedded in the APK.

## Build

Use JDK 17+, Android SDK platform 36 and Build Tools 35.0.0:

```powershell
$env:JAVA_HOME = 'C:\Program Files\Android\Android Studio\jbr'
$env:ANDROID_HOME = "$env:LOCALAPPDATA\Android\Sdk"
.\gradlew.bat :app:assembleDebug :app:testDebugUnitTest :app:lintDebug
.\gradlew.bat :app:assembleRelease :app:lintRelease
```

Use `./gradlew` on macOS/Linux. Release builds shrink unused code/resources. Without signing configuration, Gradle produces an **unsigned** optimized APK, which cannot be installed directly. CI builds that artifact for validation and a separate debug APK for development.

For an installable release, create an **untracked** `android/signing.properties`:

```properties
storeFile=/absolute/path/to/your-release.jks
storePassword=your-private-password
keyAlias=timelens
keyPassword=your-private-password
```

The current machine's persistent key is outside the repository under the user's `AppData/Local/TimeLensBuild` folder. Credentials remain local. No Play Store submission was performed.

## Verification and boundaries

Run device tests on a dedicated API 35 emulator with Chrome installed and its first-run setup completed (no account is needed):

```powershell
.\gradlew.bat :app:connectedDebugAndroidTest
```

Tests modify their own test installation's database, permission state and Accessibility service settings. Do not run against a personal tracking installation. See [CHANGELOG.md](CHANGELOG.md) for the implementation and validation record.

Background jobs are scheduled around every 15 minutes, but Doze/OEM battery policies can defer them. Expired Android source history cannot be reconstructed. Background media detail, global input counts and local PC pairing require separate integrations. Browser integration measures exposed domains only, not complete browsing history. Chrome was exercised on API 35; the other browser adapters and OEM devices still need physical-device validation.
