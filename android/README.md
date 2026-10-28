# TimeLens for Android — 8.5.0

Independent local activity tracking for Android 10+, with a native dashboard, reports, app blocking and personal settings. Android now versions independently of the Windows/web app. No account, analytics SDK, cloud backend or Internet permission is required.

## Available features

- Customizable Today modules: hourly activity, category distribution, weekly bars, focus score, app switches, longest session and yesterday comparison.
- Real icons and labels, installed-app browsing, search/sort, automatic/editable categories, app details and daily budgets.
- 7/30/90-day reports, daily averages, calendar, per-app totals and date navigation; timeline search, filters and grouped visits.
- Custom focus lengths, presets, pause/resume, saved deadlines and background completion notifications.
- Opt-in app blocking: always, during focus, after a daily app budget, or on selected weekdays/hours, including overnight schedules. A custom blocking screen, five-minute pause, blocking PIN and activity log are included.
- Background archival through Android JobScheduler, reboot/update recovery, explicit refresh and permission-health controls.
- Dark/light/system appearance, six accents, compact spacing, 12/24-hour time, detailed seconds, productive-category selection and retention.
- Privacy exclusions, collection pauses respected by later imports, deletion boundaries, range JSON/CSV exports and validated Android JSON restore.
- Adaptive launcher icon, phone tabs, wider-window navigation rail and an optimized signed release APK of approximately 2.7 MB.

## Install and permissions

Install the signed `TimeLens-Android-8.5.0.apk`. Android requires allowing installation from the application used to open it.

1. Enable Usage access when prompted to read local foreground activity.
2. For blocking, open **Focus → Blocking → Set up Android blocking**, read the disclosure and enable **TimeLens app blocking** in Android Accessibility settings. Sideloaded apps may require **App info → menu → Allow restricted settings** first.
3. Enable notifications in **Settings → Permissions & device health**. Optional precise-alarm access enables prompt timer completion; without it Android may delay alerts.
4. Choose app rules under Focus. Set a blocking PIN in Settings if desired.

The service observes package-switch events only. It does not request window contents, website text, typed characters, screenshots or browser URLs. Android Settings, the launcher, dialer and TimeLens remain accessible. Blocking is self-control assistance; it is not tamper-proof device administration.

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

Run device tests on a dedicated API 35 emulator with Chrome installed:

```powershell
.\gradlew.bat :app:connectedDebugAndroidTest
```

Tests modify their own test installation's database, permission state and Accessibility service settings. Do not run against a personal tracking installation. See [CHANGELOG.md](CHANGELOG.md) for the implementation and validation record.

Background jobs are scheduled around every 15 minutes, but Doze/OEM battery policies can defer them. Expired Android source history cannot be reconstructed. Website URL/media capture, global input counts and local PC pairing require separate integrations and are not presented as working controls.
