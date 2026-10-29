# Android 4.0.0 — UI refinement build 80704

- Appearance no longer repeats Follow system/Light/Dark choices. The top-bar sun/moon remains the theme control.
- Removed the Independent/Private/Local card and the button next to Own your attention.
- Dashboard module visibility and ordering now live under **Settings → Dashboard** and apply immediately without scanning activity again.
- Block uses three labelled Apps/Web/Activity cards, clear Turn on/Turn off actions and compact rule check buttons instead of the old strip and switch tracks. Existing PIN guards, permission disclosure and enforcement callbacks remain in place.
- History ranges use four equally sized segments. Short labels adapt to narrow windows and large text, including the year choice.
- Selected-day app history shows each app once with its combined time and visit count. Tap its chevron/card to expand that app's individual visits; collapsed apps show no repeated visits. Expanded visits remain lazy-list entries.
- Added app-history search, category filters, sorting by time/visits/name and Collapse all. Period app totals have their own collapsible section so they stay distinct from selected-day detail.

App version remains **4.0.0**. Build 80704 is signed with the same production identity as build 80703 and can update it in place. The original v4.0.0 tag remains the base release; the UI build is identified separately for reproducible source/downloads.

Verified 32 JVM tests and 21 UI/emulator checks across the refinement run, including grouped totals, expansion, sorting/search, large text in a narrow range selector, removed controls, dashboard changes, saved navigation, theme reveal and lazy-list behavior. Release build and lint passed with zero errors. These UI changes do not change Android's uninstall-protection requirements.
