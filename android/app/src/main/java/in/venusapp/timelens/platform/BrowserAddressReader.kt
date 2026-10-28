package `in`.venusapp.timelens.platform

import android.view.accessibility.AccessibilityNodeInfo
import `in`.venusapp.timelens.domain.WebAddress
import `in`.venusapp.timelens.domain.BrowserCatalog

/** Fixed address-bar IDs only. No text search, page traversal or fallback to page contents. */
object BrowserAddressReader {
    fun supported(app: String) = BrowserCatalog.supported(app)

    fun host(root: AccessibilityNodeInfo, app: String): String? {
        if (root.packageName?.toString() != app) return null
        for (id in BrowserCatalog.addressIds(app)) {
            val nodes = root.findAccessibilityNodeInfosByViewId(id)
            for (node in nodes) {
                if (node.isVisibleToUser && !node.isFocused && !node.isPassword) {
                    val host = WebAddress.host(node.text?.toString().orEmpty())
                    if (host != null) return host
                }
            }
        }
        return null
    }
}
