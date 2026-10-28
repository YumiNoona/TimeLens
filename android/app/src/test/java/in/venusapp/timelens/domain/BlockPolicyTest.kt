package `in`.venusapp.timelens.domain

import java.time.ZonedDateTime
import org.junit.Assert.*
import org.junit.Test

class BlockPolicyTest {
    private val monday = ZonedDateTime.parse("2026-10-26T10:00:00+05:30[Asia/Kolkata]")

    private fun evaluate(
        rule: BlockRule?,
        time: ZonedDateTime = monday,
        focus: Boolean = false,
        used: Long = 0,
        limit: Int = 30,
        paused: Boolean = false,
    ) = BlockPolicy.evaluate(rule, time, focus, used, limit, paused)

    @Test
    fun absentDisabledOrPausedRulesDoNotBlock() {
        assertFalse(evaluate(null).blocked)
        assertFalse(evaluate(BlockRule("app", "always", enabled = false)).blocked)
        assertFalse(evaluate(BlockRule("app", "always"), paused = true).blocked)
    }

    @Test
    fun alwaysAndFocusAreDistinct() {
        assertTrue(evaluate(BlockRule("app", "always")).blocked)
        assertFalse(evaluate(BlockRule("app", "focus")).blocked)
        assertTrue(evaluate(BlockRule("app", "focus"), focus = true).blocked)
    }

    @Test
    fun dailyBudgetBlocksAtBoundaryAndSchedulesRemainingTime() {
        assertFalse(evaluate(BlockRule("app", "budget"), used = 29 * 60_000L).blocked)
        assertEquals(
            1000L,
            evaluate(BlockRule("app", "budget"), used = 30 * 60_000L - 1000).reevaluateInMs,
        )
        assertTrue(evaluate(BlockRule("app", "budget"), used = 30 * 60_000L).blocked)
        assertEquals(
            60_000L,
            evaluate(BlockRule("app", "budget"), used = 30 * 60_000L).reevaluateInMs,
        )
        assertFalse(evaluate(BlockRule("app", "budget"), used = 99_000_000, limit = 0).blocked)
    }

    @Test
    fun scheduleUsesSelectedWeekdaysAndExclusiveEnd() {
        val rule = BlockRule("app", "schedule", startMinute = 540, endMinute = 600, days = setOf(1))
        assertTrue(evaluate(rule, monday.withHour(9)).blocked)
        assertFalse(evaluate(rule, monday).blocked)
        assertFalse(evaluate(rule, monday.plusDays(1).withHour(9)).blocked)
    }

    @Test
    fun overnightWindowBelongsToStartingDay() {
        val rule =
            BlockRule("app", "schedule", startMinute = 22 * 60, endMinute = 7 * 60, days = setOf(1))
        assertTrue(evaluate(rule, monday.withHour(23)).blocked)
        assertTrue(evaluate(rule, monday.plusDays(1).withHour(6)).blocked)
        assertFalse(evaluate(rule, monday.withHour(6)).blocked)
        assertFalse(evaluate(rule, monday.plusDays(1).withHour(7)).blocked)
    }

    @Test
    fun invalidModeDoesNotAccidentallyLockUserOut() {
        assertFalse(evaluate(BlockRule("app", "unknown")).blocked)
    }

    @Test
    fun pinHashIsSaltedAndOnlyCorrectPinVerifies() {
        val first = PinSecurity.hash("123456")
        val second = PinSecurity.hash("123456")
        assertNotEquals(first, second)
        assertTrue(PinSecurity.verify("123456", first))
        assertFalse(PinSecurity.verify("123457", first))
        assertFalse(PinSecurity.verify("123456", "corrupt"))
    }

    @Test
    fun pauseMasksRemoveOverlapsWithoutInventingTime() {
        val rows =
            UsageLedger.subtract(
                listOf(Session("app", 0, 100)),
                listOf(10L to 30L, 20L to 40L, 60L to 60L),
            )
        assertEquals(listOf(Session("app", 0, 10), Session("app", 40, 100)), rows)
        assertEquals(70L, rows.sumOf { it.milliseconds })
    }
}
