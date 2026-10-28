# Android 8.7.0 — Appearance, browser adapters and stronger blocking

- Filled/outlined navigation icons, without a persistent active-item bubble.
- Sun/moon theme toggle in the top bar; manual activity import remains in Settings.
- Proper System/Light/Dark persistence, a non-destructive animated palette, and matching native blocking-banner colors.
- Brave, Vivaldi, DuckDuckGo and additional browser-channel adapters, using an explicit package/ID allowlist rather than webpage text traversal.
- All matching website allowances are enforced; a child-domain rule cannot bypass its parent's exhausted Comfort allowance.
- Polling stops at screen-off, enforcement resumes after unlock, and temporary allowance-read errors do not silently release a matched protected app.
- Lazy app lists, bounded icon caching, batched allowance queries, safer refresh cancellation and lighter appearance-only changes.

Android Accessibility is not device-owner lockdown. The phone owner can disable the service or uninstall the app. Browser updates, hidden/private address bars and OEM restrictions can affect tracking and blocking. These adapters do not imply exhaustive browsing history or universal browser-version support.

Signing/publication is pending: the old release key is unavailable. A new signing identity cannot update the old installation in place. Never substitute a differently signed APK without clearly documenting the migration requirements. Windows/website/extension versions are unchanged.

Browser implementation references: [Chromium address bar resource](https://chromium.googlesource.com/chromium/src/+/fb56d04ed397a3db00c80b0bc1d53660c554427a/chrome/android/java/res/layout/url_bar.xml), [DuckDuckGo Android source](https://github.com/duckduckgo/Android).
