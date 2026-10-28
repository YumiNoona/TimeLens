package `in`.venusapp.timelens.data

import androidx.test.platform.app.InstrumentationRegistry
import `in`.venusapp.timelens.domain.Session
import java.time.LocalDate
import java.time.ZoneId
import org.junit.Assert.*
import org.junit.Test

class AnnualHistoryTest {
    private val context = InstrumentationRegistry.getInstrumentation().targetContext

    @Test
    fun yearSummariesKeepOldestDayDetailAndRespectShorterRetention() {
        context.deleteDatabase("activity.db")
        val raw = context.getSharedPreferences("timelens", 0)
        raw.edit().clear().commit()
        val prefs = Preferences(context)
        val today = LocalDate.now()
        val first = today.minusDays(364)
        val zone = ZoneId.systemDefault()
        val now = System.currentTimeMillis()
        val sessions = (0 until 365).flatMap { day ->
            (0 until 24).mapNotNull { visit ->
                val start = first.plusDays(day.toLong()).atStartOfDay(zone).toInstant().toEpochMilli() + visit*120_000
                val end = minOf(start+60_000, now)
                if (end > start) Session("saved.app", start, end) else null
            }
        }
        try {
            assertEquals(365, prefs.retention)
            assertFalse(prefs.grayIcons)
            ActivityStore(context).use { it.restore(sessions) }
            UsageRepository(context, prefs).use { repo ->
                val report = repo.report(today, 365)
                assertEquals(365, report.daily.size)
                assertEquals(sessions.sumOf { it.milliseconds }, report.total)
                assertEquals(sessions.size, report.apps.single().sessions)
                assertEquals(24, repo.history(first, 1).size)
                prefs.retention = 30
                val shorter = repo.report(today, 365)
                assertTrue(shorter.total < report.total)
                assertEquals(0L, shorter.daily.first().second)
                assertTrue(repo.history(first, 1).isEmpty())
            }
        } finally {
            context.deleteDatabase("activity.db")
            raw.edit().clear().commit()
        }
    }
}
