package `in`.venusapp.timelens.data

import androidx.test.platform.app.InstrumentationRegistry
import `in`.venusapp.timelens.domain.Session
import org.junit.Assert.*
import org.junit.Test

class ActivityStoreTest {
    @Test
    fun partialRangeRefreshPreservesEdgesWithoutDoubleCounting() {
        val context = InstrumentationRegistry.getInstrumentation().targetContext
        context.deleteDatabase("activity.db")
        ActivityStore(context).use { store ->
            store.replaceRange(0, 100, listOf(Session("a", 0, 100)))
            repeat(3) { store.replaceRange(20, 80, listOf(Session("b", 20, 80))) }
            assertEquals(
                listOf(Session("a", 0, 20), Session("b", 20, 80), Session("a", 80, 100)),
                store.read(0, 100),
            )
            assertEquals(100L, store.read(0, 100).sumOf { it.milliseconds })
        }
    }

    @Test
    fun versionOneDatabaseUpgradesWithoutLosingHistory() {
        val context = InstrumentationRegistry.getInstrumentation().targetContext
        context.deleteDatabase("activity.db")
        context.openOrCreateDatabase("activity.db", 0, null).use { db ->
            db.execSQL(
                "CREATE TABLE sessions(app TEXT NOT NULL,start INTEGER NOT NULL,end INTEGER NOT NULL,PRIMARY KEY(app,start))"
            )
            db.execSQL("INSERT INTO sessions VALUES('editor',10,40)")
            db.version = 1
        }
        ActivityStore(context).use { store ->
            assertEquals(listOf(Session("editor", 10, 40)), store.read(0, 100))
            store.recordBlock("browser", 50, "test")
            assertEquals(1, store.blocks(0).size)
        }
    }

    @Test
    fun exportedAndroidJsonRoundTripsAndRejectsOverlaps() {
        val day =
            DayUsage(
                java.time.LocalDate.of(2026, 1, 1),
                listOf(Session("test.app", 100, 200)),
                emptyList(),
                true,
            )
        assertEquals(day.sessions, Import.parse(Export.json(day)))
        val overlapped =
            day.copy(
                sessions = listOf(Session("test.app", 100, 200), Session("other.app", 150, 250))
            )
        try {
            Import.parse(Export.json(overlapped))
            fail("Overlaps must be rejected")
        } catch (_: IllegalArgumentException) {}
    }

    @Test
    fun explicitRestorePreservesUnrelatedHistoryAndIsIdempotent() {
        val context = InstrumentationRegistry.getInstrumentation().targetContext
        context.deleteDatabase("activity.db")
        ActivityStore(context).use { store ->
            store.replaceRange(0, 100, listOf(Session("a", 0, 100)))
            repeat(2) { store.restore(listOf(Session("b", 20, 30), Session("c", 70, 80))) }
            assertEquals(100L, store.read(0, 100).sumOf { it.milliseconds })
            assertEquals(5, store.read(0, 100).size)
        }
    }

    @Test
    fun reimportIsIdempotentAndDeletionBoundarySurvives() {
        val context = InstrumentationRegistry.getInstrumentation().targetContext
        context.deleteDatabase("activity.db")
        ActivityStore(context).use { store ->
            val rows = listOf(Session("editor", 10, 30), Session("browser", 40, 60))
            repeat(3) { store.replaceRange(0, 100, rows) }
            assertEquals(rows, store.read(0, 100))
            assertEquals(
                listOf(Session("editor", 20, 30), Session("browser", 40, 50)),
                store.read(20, 50),
            )
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
