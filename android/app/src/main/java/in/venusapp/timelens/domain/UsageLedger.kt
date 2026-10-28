package `in`.venusapp.timelens.domain

data class Observation(val at: Long, val kind: Kind, val app: String = "", val activity: String = "") {
    enum class Kind { RESUME, PAUSE, SCREEN_OFF, LOCK, SHUTDOWN, STARTUP }
}
data class Session(val app: String, val start: Long, val end: Long) {
    val milliseconds: Long get() = (end - start).coerceAtLeast(0)
}

/** Exclusive foreground ledger. Gaps, screen-off time and overlapping activities never add time. */
object UsageLedger {
    fun build(events: List<Observation>, from: Long, until: Long): List<Session> {
        if (until <= from) return emptyList()
        val result = mutableListOf<Session>()
        var current: Observation? = null
        fun close(at: Long) {
            current?.let { active ->
                val start = maxOf(from, active.at)
                val end = minOf(until, at)
                if (end > start) {
                    val last = result.lastOrNull()
                    if (last?.app == active.app && last.end == start) {
                        result[result.lastIndex] = last.copy(end = end)
                    } else result += Session(active.app, start, end)
                }
            }
            current = null
        }
        events.sortedBy { it.at }.forEach { event ->
            if (event.at > until) return@forEach
            when (event.kind) {
                Observation.Kind.RESUME -> {
                    if (event.app.isNotBlank() && (current?.app != event.app || current?.activity != event.activity)) {
                        close(event.at)
                        current = event
                    }
                }
                Observation.Kind.PAUSE -> if (current?.app == event.app && current?.activity == event.activity) close(event.at)
                else -> close(event.at)
            }
        }
        close(until)
        return result
    }
}
