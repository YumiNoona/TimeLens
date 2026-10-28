package `in`.venusapp.timelens.data

import androidx.test.platform.app.InstrumentationRegistry
import `in`.venusapp.timelens.domain.Session
import org.junit.Assert.*
import org.junit.Test

class ActivityStoreTest {
    @Test fun reimportIsIdempotentAndDeletionBoundarySurvives() {
        val context = InstrumentationRegistry.getInstrumentation().targetContext
        context.deleteDatabase("activity.db")
        ActivityStore(context).use { store ->
            val rows = listOf(Session("editor", 10, 30), Session("browser", 40, 60))
            repeat(3) { store.replaceRange(0, 100, rows) }
            assertEquals(rows, store.read(0, 100))
            assertEquals(listOf(Session("editor", 20, 30), Session("browser", 40, 50)), store.read(20, 50))
            store.exclude("browser")
            assertEquals(listOf(rows.first()), store.read(0, 100))
            store.clear()
            assertTrue(store.read(0, 100).isEmpty())
        }
        val prefs = Preferences(context)
        val repository = UsageRepository(context, prefs)
        repository.clear()
        assertTrue(Preferences(context).since > System.currentTimeMillis() - 5000)
        assertEquals(0L, Preferences(context).focusEnd)
        prefs.excluded = setOf("secret")
        prefs.restore("secret", 1234L)
        assertFalse(Preferences(context).excluded.contains("secret"))
        assertEquals(1234L, Preferences(context).appSince("secret"))
        repository.close()
    }
}
