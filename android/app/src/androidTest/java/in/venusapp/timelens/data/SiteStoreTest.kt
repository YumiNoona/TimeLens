package `in`.venusapp.timelens.data

import androidx.test.platform.app.InstrumentationRegistry
import org.junit.Assert.*
import org.junit.Test

class SiteStoreTest {
    private val context = InstrumentationRegistry.getInstrumentation().targetContext

    @Test
    fun mergesObservationsAndClipsRollingWindow() {
        SiteStore(context).use {
            it.clear()
            it.observe("example.com", 1000, 2000)
            it.observe("example.com", 2000, 3000)
            it.observe("other.com", 3000, 4000)
            val rows = it.read(1500, 3500)
            assertEquals(2, rows.size)
            assertEquals(1500L, rows.first().milliseconds)
            assertEquals(500L, rows.last().milliseconds)
            assertEquals("example.com", rows.first().app)
            it.clear()
        }
    }

    @Test
    fun privatePathsCannotEnterWebsiteStorage() {
        SiteStore(context).use {
            try {
                it.observe("https://example.com/private?q=secret", 1000, 2000)
                fail("Only canonical domains may be stored")
            } catch (_: IllegalArgumentException) {}
        }
    }

    @Test
    fun retentionDeletionAndOverlayCoverageStayIndependent() {
        SiteStore(context).use {
            it.clear()
            it.observe("example.com", 1000, 2000)
            it.observe("other.com", 3000, 4000)
            it.cover("coverage.test", 1000, 2000)
            it.prune(2500)
            assertEquals(1, it.read(0, 5000).size)
            assertTrue(it.covered("coverage.test", 0, 5000).isEmpty())
            it.cover("coverage.test", 3000, 4000)
            it.clear()
            assertTrue(it.read(0, 5000).isEmpty())
            assertEquals(listOf(3000L to 4000L), it.covered("coverage.test", 0, 5000))
            it.prune(5000)
        }
    }
}
