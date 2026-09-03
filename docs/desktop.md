# TimeLens desktop guide

## Updating safely

Use **Settings → Update now**. TimeLens downloads the signed release executable over HTTPS, verifies its size and SHA-256 hash, closes the running tray process, replaces the executable, and starts the new version with a fresh dashboard page. The previous browser tab waits for the local API to return and reloads itself. If Windows prevents the replacement, the updater stops with an error instead of starting an old executable; install the current `TimeLens-Setup.exe` from the release page in that case.

Update helper diagnostics are stored locally in `%LOCALAPPDATA%\TimeLens\updates\last-update.log`. Activity history and settings remain in `%LOCALAPPDATA%\TimeLens\activity.db`.

## Block protection

Enable **Settings → Block password** before sharing a PC. The password uses PBKDF2-SHA256 with a per-install random salt. It is required to turn off protection, reduce a block action, remove a target, or exit through the TimeLens tray menu. To exit while protection is active, open **Block**, choose **Exit**, and enter the password.

This protects against casual bypasses in the TimeLens UI and tray menu. It cannot stop someone with access to the same Windows account from ending a user process in Task Manager, changing local files, or using an administrator account. Use separate Windows accounts and a locked screen for stronger device security.

## Dashboard addresses

TimeLens intentionally listens only on the local computer at `http://127.0.0.1:47821`; it does not publish activity data to the internet. Modern browsers also resolve a friendly local address such as `http://veil.timelens.dashboard.localhost:47821` without DNS setup.

For the exact short name `veil.timelens.dashboard`, add this line to the Windows hosts file as an administrator, then open the same port in the browser:

```text
127.0.0.1 veil.timelens.dashboard
```

The hosts file is `%SystemRoot%\System32\drivers\etc\hosts`. This alias stays local to that PC. Do not point a public DNS record at a local dashboard: the service is designed for loopback-only access.

## How activity is classified

TimeLens records foreground windows, input inactivity, browser extension events, audible media, and Windows session changes. Time is assigned only while a real app has focus. Windows shell surfaces such as Explorer, Search, Start, and TimeLens are excluded from Top Apps and Categories. Browser events count only while their matching browser owns the foreground window, while dedicated idle spans remove unattended time.

Built-in rules cover common games and launchers, hardware utilities, development tools, creative tools, browsers, cloud storage, learning services, and streaming sites. Add or override any rule from **Rules**; user rules take priority over community defaults.
