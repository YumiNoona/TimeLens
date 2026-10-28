package `in`.venusapp.timelens.domain

import java.time.ZonedDateTime
import org.junit.Assert.*
import org.junit.Test

class WebAddressTest {
    @Test fun comfortChildDoesNotHideAnExhaustedParentAllowance() {
        val parent = BlockRule("web:example.com", "comfort", allowanceMinutes = 15)
        val child = BlockRule("web:video.example.com", "comfort", allowanceMinutes = 60)
        val matches = WebAddress.matchingRules(listOf(parent, child), "video.example.com")
        assertEquals(listOf(child, parent), matches)
        val now = ZonedDateTime.parse("2026-10-28T10:00:00+05:30[Asia/Kolkata]")
        assertTrue(matches.any {
            BlockPolicy.evaluate(it, now, false, 20 * 60_000L, 0, false).blocked
        })
        assertTrue(WebAddress.matchingRules(listOf(parent), "notexample.com").isEmpty())
    }
    @Test
    fun savesHostOnlyAndNormalizesDomains() {
        assertEquals(
            "example.com",
            WebAddress.host("https://www.Example.COM/private?q=secret#part"),
        )
        assertEquals("example.com", WebAddress.host("example.com"))
        assertEquals("xn--bcher-kva.de", WebAddress.host("https://bücher.de/path"))
    }

    @Test
    fun rejectsSearchesCredentialsAndInternalPages() {
        listOf(
                "search words",
                "about:blank",
                "chrome://newtab",
                "file:///secret",
                "https://user:pass@example.com",
                "https://",
                "example..com",
                "-bad.com",
            )
            .forEach { assertNull(it, WebAddress.host(it)) }
    }

    @Test
    fun subdomainBoundariesDoNotMatchLookalikes() {
        assertTrue(WebAddress.matches("video.example.com", "example.com"))
        assertFalse(WebAddress.matches("notexample.com", "example.com"))
        assertFalse(WebAddress.matches("example.com.evil.org", "example.com"))
    }

    @Test
    fun strictParentCannotBeBypassedByComfortChildRule() {
        val strict = BlockRule("web:example.com", "strict")
        val comfort = BlockRule("web:video.example.com", "comfort")
        assertEquals(strict, WebAddress.rule(listOf(comfort, strict), "video.example.com"))
        assertNull(WebAddress.rule(listOf(strict.copy(enabled = false)), "example.com"))
    }

    @Test
    fun comfortAndStrictIgnoreOldGlobalPause() {
        val now = ZonedDateTime.parse("2026-10-28T10:00:00+05:30[Asia/Kolkata]")
        fun decide(mode: String, used: Long) =
            BlockPolicy.evaluate(
                BlockRule("app", mode, allowanceMinutes = 15),
                now,
                false,
                used,
                0,
                true,
            )
        assertTrue(decide("strict", 0).blocked)
        assertFalse(decide("comfort", 899_000).blocked)
        assertEquals(1000L, decide("comfort", 899_000).reevaluateInMs)
        assertTrue(decide("comfort", 900_000).blocked)
    }
}
