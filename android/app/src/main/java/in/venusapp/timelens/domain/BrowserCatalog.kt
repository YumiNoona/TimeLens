package `in`.venusapp.timelens.domain

/** Explicit adapters only: unknown packages never gain permission to read arbitrary text. */
object BrowserCatalog {
    private val chromium = setOf(
        "com.android.chrome", "com.chrome.beta", "com.chrome.dev", "com.chrome.canary",
        "com.brave.browser", "com.brave.browser_beta", "com.brave.browser_nightly",
        "com.microsoft.emmx", "com.microsoft.emmx.beta", "com.microsoft.emmx.dev",
        "com.vivaldi.browser", "com.vivaldi.browser.snapshot",
    )
    private val firefox = setOf("org.mozilla.firefox", "org.mozilla.firefox_beta", "org.mozilla.fenix", "org.mozilla.fennec_fdroid")
    fun addressIds(app: String): List<String> = when {
        app in chromium -> listOf("$app:id/url_bar")
        app in firefox -> listOf("$app:id/mozac_browser_toolbar_url_view")
        app == "com.sec.android.app.sbrowser" || app == "com.sec.android.app.sbrowser.beta" ->
            listOf("$app:id/location_bar_edit_text")
        app == "com.duckduckgo.mobile.android" -> listOf("$app:id/omnibarTextInput", "com.duckduckgo.app.browser:id/omnibarTextInput")
        else -> emptyList()
    }
    fun supported(app: String) = addressIds(app).isNotEmpty()
    const val description = "Chrome, Brave, Firefox, Edge, Samsung Internet, Vivaldi and DuckDuckGo"
}
