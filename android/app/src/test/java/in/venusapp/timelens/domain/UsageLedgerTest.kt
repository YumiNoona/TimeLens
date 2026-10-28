package `in`.venusapp.timelens.domain

import `in`.venusapp.timelens.domain.Observation.Kind.*
import org.junit.Assert.*
import org.junit.Test

class UsageLedgerTest {
    private fun event(
        at: Long,
        kind: Observation.Kind,
        app: String = "a",
        activity: String = "main",
    ) = Observation(at, kind, app, activity)

    @Test
    fun switchesAreExclusiveAndGapsRemainGaps() {
        val rows =
            UsageLedger.build(
                listOf(
                    event(0, RESUME),
                    event(10, RESUME, "b"),
                    event(20, PAUSE, "b"),
                    event(30, RESUME),
                    event(40, PAUSE),
                ),
                0,
                50,
            )
        assertEquals(listOf(Session("a", 0, 10), Session("b", 10, 20), Session("a", 30, 40)), rows)
        assertEquals(30L, rows.sumOf { it.milliseconds })
    }

    @Test
    fun clipsMidnightAndLiveSessionToQueryWindow() {
        assertEquals(
            listOf(Session("a", 100, 200)),
            UsageLedger.build(listOf(event(50, RESUME)), 100, 200),
        )
    }

    @Test
    fun screenOffLockShutdownAndStartupStopAccounting() {
        listOf(SCREEN_OFF, LOCK, SHUTDOWN, STARTUP).forEach { kind ->
            assertEquals(
                listOf(Session("a", 0, 10)),
                UsageLedger.build(listOf(event(0, RESUME), event(10, kind)), 0, 100),
            )
        }
    }

    @Test
    fun duplicateResumeDoesNotTruncateSession() {
        assertEquals(
            listOf(Session("a", 0, 30)),
            UsageLedger.build(listOf(event(0, RESUME), event(10, RESUME), event(30, PAUSE)), 0, 50),
        )
    }

    @Test
    fun staleActivityPauseDoesNotCloseNewForegroundActivity() {
        val rows =
            UsageLedger.build(
                listOf(
                    event(0, RESUME),
                    event(10, RESUME, "a", "detail"),
                    event(11, PAUSE),
                    event(20, PAUSE, "a", "detail"),
                ),
                0,
                50,
            )
        assertEquals(listOf(Session("a", 0, 20)), rows)
    }

    @Test
    fun unmatchedPauseNeverFabricatesTime() {
        assertTrue(UsageLedger.build(listOf(event(10, PAUSE)), 0, 50).isEmpty())
        assertTrue(UsageLedger.build(emptyList(), 0, 50).isEmpty())
        assertTrue(UsageLedger.build(listOf(event(0, RESUME)), 50, 0).isEmpty())
    }

    @Test
    fun sortsEventsAndIgnoresFutureObservations() {
        assertEquals(
            listOf(Session("a", 10, 20)),
            UsageLedger.build(
                listOf(event(100, RESUME, "b"), event(20, PAUSE), event(10, RESUME)),
                0,
                50,
            ),
        )
    }

    @Test
    fun privateAppEventsStillBoundVisibleForegroundTime() {
        val rows =
            UsageLedger.build(
                    listOf(
                        event(0, RESUME),
                        event(10, RESUME, "secret"),
                        event(20, PAUSE, "secret"),
                    ),
                    0,
                    50,
                )
                .filter { it.app != "secret" }
        assertEquals(listOf(Session("a", 0, 10)), rows)
    }
}
