package `in`.venusapp.timelens.domain

import org.junit.Assert.*
import org.junit.Test

class BrowserCatalogTest {
    @Test fun recognizesExplicitBrowserVariants() {
        listOf("com.brave.browser", "com.brave.browser_beta", "com.brave.browser_nightly",
            "com.vivaldi.browser", "org.mozilla.fenix", "com.duckduckgo.mobile.android",
            "com.chrome.canary", "com.microsoft.emmx").forEach { assertTrue(it, BrowserCatalog.supported(it)) }
        assertEquals(listOf("com.brave.browser:id/url_bar"), BrowserCatalog.addressIds("com.brave.browser"))
    }
    @Test fun neverGuessesFromAPackageName() {
        assertFalse(BrowserCatalog.supported("fake.brave.browser"))
        assertFalse(BrowserCatalog.supported("com.brave.browser.fake"))
        assertTrue(BrowserCatalog.addressIds("untrusted.app").isEmpty())
    }
}
