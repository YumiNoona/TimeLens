package `in`.venusapp.timelens.domain

import java.time.LocalDate
import java.time.Instant
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
    ): List<Pair<LocalDate, Long>> {
        if (days <= 0) return emptyList()
        val totals = LongArray(days)
        val first = from.atStartOfDay(zone).toInstant().toEpochMilli()
        val limit = from.plusDays(days.toLong()).atStartOfDay(zone).toInstant().toEpochMilli()
        // Visit each interval once, splitting only actual midnight crossings. This avoids
        // rescanning an entire year of sessions once for every calendar cell.
        sessions.forEach { session ->
            var cursor = maxOf(first, session.start)
            val end = minOf(limit, session.end)
            while (cursor < end) {
                val date = Instant.ofEpochMilli(cursor).atZone(zone).toLocalDate()
                val next = minOf(end, date.plusDays(1).atStartOfDay(zone).toInstant().toEpochMilli())
                val index = java.time.temporal.ChronoUnit.DAYS.between(from, date).toInt()
                if (index in totals.indices) totals[index] += next - cursor
                cursor = next
            }
        }
        return (0 until days).map { from.plusDays(it.toLong()) to totals[it] }
    }

    fun longest(sessions: List<Session>): Long = sessions.maxOfOrNull { it.milliseconds } ?: 0

    fun switches(sessions: List<Session>): Int =
        sessions.zipWithNext().count { (a, b) -> a.app != b.app && b.start - a.end in 0..60_000 }
}
