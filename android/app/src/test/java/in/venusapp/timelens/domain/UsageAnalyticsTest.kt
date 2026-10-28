package `in`.venusapp.timelens.domain

import java.time.*
import org.junit.Assert.*
import org.junit.Test

class UsageAnalyticsTest {
    @Test
    fun fullYearBucketsPreserveEveryIntervalAndEmptyDay() {
        val zone = ZoneId.of("Asia/Kolkata")
        val first = LocalDate.of(2025, 11, 1)
        val rows = (0 until 365).flatMap { day ->
            if (day == 180) emptyList() else (0 until 200).map { visit ->
                val start = first.plusDays(day.toLong()).atStartOfDay(zone).toInstant().toEpochMilli() + visit*120_000
                Session("app", start, start+60_000)
            }
        }
        val result = UsageAnalytics.daily(rows, first, 365, zone)
        assertEquals(365, result.size)
        assertEquals(0L, result[180].second)
        assertEquals(12_000_000L, result.first().second)
        assertEquals(rows.sumOf { it.milliseconds }, result.sumOf { it.second })
    }

    @Test
    fun dailyBucketsRespectDstAndRangeClipping() {
        val zone = ZoneId.of("America/New_York")
        val first = LocalDate.of(2026, 3, 7)
        val start = first.minusDays(1).atStartOfDay(zone).toInstant().toEpochMilli()
        val end = first.plusDays(4).atStartOfDay(zone).toInstant().toEpochMilli()
        assertEquals(listOf(86_400_000L, 82_800_000L, 86_400_000L),
            UsageAnalytics.daily(listOf(Session("app", start, end)), first, 3, zone).map { it.second })
    }
    @Test
    fun hourlyAndDailyTotalsConserveMidnightClipping() {
        val zone = ZoneId.of("Asia/Kolkata")
        val day = LocalDate.of(2026, 10, 28)
        val midnight = day.atStartOfDay(zone).toInstant().toEpochMilli()
        val rows =
            listOf(
                Session("a", midnight - 60_000, midnight + 60_000),
                Session("b", midnight + 3_600_000, midnight + 3_660_000),
            )
        val hours = UsageAnalytics.hourly(rows, day, zone)
        assertEquals(60_000L, hours[0])
        assertEquals(60_000L, hours[1])
        assertEquals(120_000L, hours.sum())
        assertEquals(120_000L, UsageAnalytics.daily(rows, day, 1, zone).single().second)
    }

    @Test
    fun repeatedDstHourAccumulatesBothOccurrences() {
        val zone = ZoneId.of("America/New_York")
        val day = LocalDate.of(2026, 11, 1)
        val from =
            ZonedDateTime.of(day, LocalTime.of(1, 0), zone)
                .withEarlierOffsetAtOverlap()
                .toInstant()
                .toEpochMilli()
        val until = ZonedDateTime.of(day, LocalTime.of(2, 0), zone).toInstant().toEpochMilli()
        val hours = UsageAnalytics.hourly(listOf(Session("a", from, until)), day, zone)
        assertEquals(7_200_000L, hours[1])
        assertEquals(7_200_000L, hours.sum())
    }

    @Test
    fun missingDstHourDoesNotCreateTime() {
        val zone = ZoneId.of("America/New_York")
        val day = LocalDate.of(2026, 3, 8)
        val start = day.atStartOfDay(zone).toInstant().toEpochMilli()
        val end = day.plusDays(1).atStartOfDay(zone).toInstant().toEpochMilli()
        val hours = UsageAnalytics.hourly(listOf(Session("a", start, end)), day, zone)
        assertEquals(0L, hours[2])
        assertEquals(23 * 3_600_000L, hours.sum())
    }

    @Test
    fun appSwitchesDoNotCountLongUnobservedGaps() {
        assertEquals(
            1,
            UsageAnalytics.switches(
                listOf(Session("a", 0, 10), Session("b", 10, 20), Session("a", 100_000, 100_010))
            ),
        )
    }
}
