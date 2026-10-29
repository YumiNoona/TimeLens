# TimeLens Android 5.0.0 — input refinements

- Restored native switches for Blocking, Website tracking and individual rules. Settings switches also respond to their entire label row, with one accessible switch per setting.
- Restored Material touch feedback throughout the app. Filter sort labels are full-width tap targets.
- Search opens ready to type when explicitly requested, includes a clear action and uses the keyboard's Search action to finish typing without losing the query. Returning to a saved search does not reopen the keyboard.
- Search inputs avoid automatic spelling/capitalization changes. Website rules use a URL keyboard; numeric fields use numeric keyboards with Done actions. Invalid allowance and budget values show errors.
- PIN dialogs focus the PIN field and accept keyboard Done through the same verification and retry limit as Unlock. Submitting while verification is running does not start another attempt.
- Content adjusts for the keyboard, including the bottom navigation, rather than letting the keyboard cover the page.

App version remains 5.0.0; build 80706 updates the signed 5.0.0 and 4.0.0 releases in place. The base `android-v5.0.0` tag remains unchanged; the refinement is identified separately.

Verified 32 JVM tests and 17 UI checks across the refinement runs, including switch row taps, saved search focus, keyboard actions, PIN retries, protected rule changes, navigation, grouped history and lazy lists. Release build and lint passed. Two UI checks were updated to locate lazy-list rules correctly and account for the calendar removed in the base 5.0.0 release.
