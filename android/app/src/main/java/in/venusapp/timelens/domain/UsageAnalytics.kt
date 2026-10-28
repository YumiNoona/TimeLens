package `in`.venusapp.timelens.domain

import java.time.LocalDate
import java.time.ZoneId

object UsageAnalytics {
    fun hourly(sessions: List<Session>, date: LocalDate, zone: ZoneId): List<Long> {
        val buckets = MutableList(24) { 0L }
        var cursor = date.atStartOfDay(zone)
        val limit = date.plusDays(1).atStartOfDay(zone)
        while (cursor < limit) {
            val next = minOf(cursor.plusHours(1), limit)
            val from = cursor.toInstant().toEpochMilli()
            val until = next.toInstant().toEpochMilli()
            buckets[cursor.hour] += sessions.sumOf {
                (minOf(it.end, until) - maxOf(it.start, from)).coerceAtLeast(0)
            }
            cursor = next
        }
        return buckets
    }

    fun daily(
        sessions: List<Session>,
        from: LocalDate,
        days: Int,
        zone: ZoneId,
    ): List<Pair<LocalDate, Long>> =
        (0 until days).map { offset ->
            val date = from.plusDays(offset.toLong())
            val start = date.atStartOfDay(zone).toInstant().toEpochMilli()
            val end = date.plusDays(1).atStartOfDay(zone).toInstant().toEpochMilli()
            date to
                sessions.sumOf { (minOf(end, it.end) - maxOf(start, it.start)).coerceAtLeast(0) }
        }

    fun longest(sessions: List<Session>): Long = sessions.maxOfOrNull { it.milliseconds } ?: 0

    fun switches(sessions: List<Session>): Int =
        sessions.zipWithNext().count { (a, b) -> a.app != b.app && b.start - a.end in 0..60_000 }
}
