package `in`.venusapp.timelens.platform

import android.view.accessibility.AccessibilityNodeInfo
import `in`.venusapp.timelens.domain.WebAddress

/** Fixed address-bar IDs only. No text search, page traversal or fallback to page contents. */
object BrowserAddressReader {
    private val ids =
        mapOf(
            "com.android.chrome" to listOf("url_bar"),
            "com.chrome.beta" to listOf("url_bar"),
            "com.chrome.dev" to listOf("url_bar"),
            "org.mozilla.firefox" to listOf("mozac_browser_toolbar_url_view"),
            "com.microsoft.emmx" to listOf("url_bar"),
            "com.sec.android.app.sbrowser" to listOf("location_bar_edit_text"),
        )

    fun supported(app: String) = app in ids

    fun host(root: AccessibilityNodeInfo, app: String): String? {
        if (root.packageName?.toString() != app) return null
        for (id in ids[app].orEmpty()) {
            val nodes = root.findAccessibilityNodeInfosByViewId("$app:id/$id")
            for (node in nodes) {
                if (node.isVisibleToUser && !node.isFocused) {
                    val host = WebAddress.host(node.text?.toString().orEmpty())
                    if (host != null) return host
                }
            }
        }
        return null
    }
}
