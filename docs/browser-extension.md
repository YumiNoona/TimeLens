# Browser companions

Version 9.0.0 adds local website context and configured website blocking to TimeLens for Windows. Chrome, Edge and Brave packages share the Chromium implementation; Firefox uses the same canonical collectors with its browser-specific manifest. Store submission is performed by the maintainer.

## Package and install

Run scripts/package-extensions.ps1 -OutputDirectory <directory>. Each ZIP contains its manifest at the root, packaged code and icons. Chromium packages can be extracted and loaded from the browser's Extensions page using Load unpacked. Normal Firefox release installations require a Mozilla-signed add-on; the Firefox ZIP is the store submission source, not a signed XPI. The existing Firefox listing may remain on its previous version until the maintainer publishes the update.

Keep TimeLens running, generate a code in Settings → Privacy center and enter it in the extension popup. Pair each browser profile separately. Pairing survives normal restarts. Private/incognito tabs are excluded.

## Collection and permissions

The companion reports focused website observations, aggregate keystroke/click counts and supported media timing to 127.0.0.1:47821 only. URL detail, titles, exclusions and collection settings follow desktop privacy choices; typed characters, form contents and passwords are never read. Queued aggregate batches are validated, bounded, privacy-filtered and deduplicated by ID. Media tabs are ended when closed. Dormant pages do not run keep-alive polling.

Tabs identify selected websites; storage preserves pairing and pending aggregate counts; alarms provide bounded recovery; HTTP/HTTPS access and Chromium scripting support collection and configured reminders/blocks. There is no remote executable code. Provide the public privacy policy and describe local browsing/activity handling in store forms.

Official distribution references: [Chrome](https://developer.chrome.com/docs/extensions/how-to/distribute), [Edge](https://learn.microsoft.com/en-us/microsoft-edge/extensions/publish/hosting-and-updating), [Firefox signing](https://extensionworkshop.com/documentation/publish/signing-and-distribution-overview/).
