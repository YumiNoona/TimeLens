package `in`.venusapp.timelens.data

import android.app.AppOpsManager
import android.app.usage.UsageEvents
import android.app.usage.UsageStatsManager
import android.content.Context
import android.os.Process
import `in`.venusapp.timelens.domain.Observation
import `in`.venusapp.timelens.domain.Session
import `in`.venusapp.timelens.domain.UsageLedger
import java.time.LocalDate
import java.time.ZoneId

data class AppUsage(val app: String, val label: String, val milliseconds: Long, val sessions: Int, val category: String, val limit: Int)
data class DayUsage(val date: LocalDate, val sessions: List<Session>, val apps: List<AppUsage>, val hasAccess: Boolean) {
    val total: Long get() = sessions.sumOf { it.milliseconds }
}

class UsageRepository(private val context: Context, val preferences: Preferences) : AutoCloseable {
    private val store = ActivityStore(context)
    private val labels = mutableMapOf<String, String>()
    fun hasAccess(): Boolean = context.getSystemService(AppOpsManager::class.java)
        .unsafeCheckOpNoThrow(AppOpsManager.OPSTR_GET_USAGE_STATS, Process.myUid(), context.packageName) == AppOpsManager.MODE_ALLOWED
    fun label(app: String): String = labels.getOrPut(app) {
        try { context.packageManager.getApplicationLabel(context.packageManager.getApplicationInfo(app, 0)).toString() }
        catch (_: android.content.pm.PackageManager.NameNotFoundException) { app.substringAfterLast('.') }
    }
    fun load(date: LocalDate): DayUsage {
        val now = System.currentTimeMillis()
        val access = hasAccess()
        val zone = ZoneId.systemDefault()
        // Refresh the last two days only. Older local archives must survive OS event expiry.
        val refreshFrom = maxOf(LocalDate.now().minusDays(1).atStartOfDay(zone).toInstant().toEpochMilli(), preferences.since)
        val selectedFrom = maxOf(date.atStartOfDay(zone).toInstant().toEpochMilli(), preferences.since)
        if (access) {
            importRange(refreshFrom, now)
            if (date >= LocalDate.now().minusDays(6) && selectedFrom < refreshFrom) {
                importRange(selectedFrom, minOf(date.plusDays(1).atStartOfDay(zone).toInstant().toEpochMilli(), refreshFrom))
            }
        }
        val retentionCutoff = LocalDate.now().minusDays(preferences.retention.toLong() - 1).atStartOfDay(zone).toInstant().toEpochMilli()
        store.prune(maxOf(retentionCutoff, preferences.since))
        val from = maxOf(selectedFrom, retentionCutoff)
        val until = minOf(date.plusDays(1).atStartOfDay(zone).toInstant().toEpochMilli(), now)
        val sessions = if (from < until) store.read(from, until).filter { it.app !in preferences.excluded } else emptyList()
        val apps = sessions.groupBy { it.app }.map { (app, rows) ->
            AppUsage(app, label(app), rows.sumOf { it.milliseconds }, rows.size, preferences.category(app), preferences.limit(app))
        }.sortedByDescending { it.milliseconds }
        return DayUsage(date, sessions, apps, access)
    }
    private fun importRange(from: Long, until: Long) {
        if (from >= until) return
        val manager = context.getSystemService(UsageStatsManager::class.java)
        val events = manager.queryEvents((from - 86_400_000L).coerceAtLeast(0), until) ?: return
        val observations = mutableListOf<Observation>()
        val event = UsageEvents.Event()
        while (events.hasNextEvent()) {
            events.getNextEvent(event)
            val kind = when (event.eventType) {
                UsageEvents.Event.ACTIVITY_RESUMED -> Observation.Kind.RESUME
                UsageEvents.Event.ACTIVITY_PAUSED, UsageEvents.Event.ACTIVITY_STOPPED -> Observation.Kind.PAUSE
                UsageEvents.Event.SCREEN_NON_INTERACTIVE -> Observation.Kind.SCREEN_OFF
                UsageEvents.Event.KEYGUARD_SHOWN -> Observation.Kind.LOCK
                UsageEvents.Event.DEVICE_SHUTDOWN -> Observation.Kind.SHUTDOWN
                UsageEvents.Event.DEVICE_STARTUP -> Observation.Kind.STARTUP
                else -> null
            }
            if (kind != null) observations += Observation(event.timeStamp, kind, event.packageName ?: "", event.className ?: "")
        }
        val sessions = UsageLedger.build(observations, from, until)
            .filter { it.app !in preferences.excluded }
            .map { it.copy(start = maxOf(it.start, preferences.appSince(it.app))) }
            .filter { it.end > it.start }
        store.replaceRange(from, until, sessions)
    }
    fun exclude(app: String) {
        preferences.excluded = preferences.excluded + app
        store.exclude(app)
    }
    fun clear() {
        // Keep the deletion boundary so the next refresh cannot re-import deleted OS history.
        preferences.since = System.currentTimeMillis()
        preferences.focusEnd = 0
        store.clear()
    }
    override fun close() { store.close() }
}
