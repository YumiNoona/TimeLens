# Chrome Web Store submission - TimeLens 7.0.0

Upload `TimeLens-Chrome-Extension.zip` from the repository root. The manifest is
at the ZIP root. For local testing extract it, open chrome://extensions, enable
Developer mode and choose Load unpacked. Run TimeLens 7.0 on the same PC.

Suggested listing:
- Name: TimeLens Tracker
- Summary: Private website activity tracking and focus reminders for TimeLens.
- Single purpose: Add website-level time reports and website focus controls to the local TimeLens activity tracker.
- Description: See time, clicks and keystroke counts for websites in TimeLens. Activity stays on your computer. Track focused tabs, check desktop connectivity and use optional website reminders or strict focus blocks. Requires TimeLens for Windows and Chrome 120 or newer. Private windows are excluded. No account, analytics or cloud activity storage.

Permission justifications:
- tabs: read the selected URL/title and observe selection/navigation.
- alarms: recover activity/settings heartbeats after worker restart.
- storage: remove the legacy local replay queue during upgrades.
- scripting and HTTP/HTTPS hosts: count trusted clicks/keystrokes on focused pages, display configured focus reminders and clear them when disabled. Host access also permits the loopback desktop connection. No remote executable code is loaded.

Privacy disclosure: browsing history (URLs, titles and domains) and aggregate user-activity counts (clicks and keystrokes) goes only to
http://127.0.0.1:47821 on the user's computer and persists in the local desktop
SQLite database under configured retention. No activity is sold or sent to analytics
or advertising servers. Disclose browsing-history and user-activity handling in the store privacy form. No typed characters, key values, passwords or form contents are read. Input capture follows the desktop input setting. Undelivered input batches retry in memory for at most two minutes.

Deploy `web/public/privacy.html` with the website and verify the public policy URL
before submission. Add real extension/dashboard screenshots, reviewer instructions
to install/start the desktop app, a support contact and required developer account
verification. Google review and account submission remain separate steps.

## Firefox update

Submit `TimeLens-Firefox-Extension.zip` as a new version of the existing TimeLens
Tracker add-on in the Mozilla developer hub, preserving that listing's assigned
add-on identity. Both manifests declare 7.0.0. The Firefox package includes the same
Notify/Strict controls, input collector and connection popup. Apply the same privacy
disclosures and reviewer instructions. The GitHub ZIP is unsigned; release Firefox
requires Mozilla signing. For temporary testing use about:debugging > This Firefox >
Load Temporary Add-on and select the extracted manifest.json.

Firefox 140+ is required so its built-in install/update consent can disclose browsing
and website activity sent to the companion desktop app. The manifest declares
`browsingActivity` and `websiteActivity`; local-only transmission is still disclosed.
See [Mozilla's built-in consent documentation](https://extensionworkshop.com/documentation/develop/firefox-builtin-data-consent/).
