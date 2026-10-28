package `in`.venusapp.timelens.data

import android.app.AppOpsManager
import android.app.usage.UsageEvents
import android.app.usage.UsageStatsManager
import android.content.Context
import android.content.Intent
import android.os.Process
import `in`.venusapp.timelens.domain.Observation
import `in`.venusapp.timelens.domain.Session
import `in`.venusapp.timelens.domain.UsageLedger
import java.time.Instant
import java.time.LocalDate
import java.time.ZoneId
import java.util.concurrent.ConcurrentHashMap

data class InstalledApp(val app: String, val label: String)

data class AppUsage(
    val app: String,
    val label: String,
    val milliseconds: Long,
    val sessions: Int,
    val category: String,
    val limit: Int,
)

data class DayUsage(
    val date: LocalDate,
    val sessions: List<Session>,
    val apps: List<AppUsage>,
    val hasAccess: Boolean,
) {
    val total: Long
        get() = sessions.sumOf { it.milliseconds }
}

class UsageRepository(private val context: Context, val preferences: Preferences) : AutoCloseable {
    private val store = ActivityStore(context)
    private val labels = ConcurrentHashMap<String, String>()

    fun hasAccess(): Boolean =
        context
            .getSystemService(AppOpsManager::class.java)
            .unsafeCheckOpNoThrow(
                AppOpsManager.OPSTR_GET_USAGE_STATS,
                Process.myUid(),
                context.packageName,
            ) == AppOpsManager.MODE_ALLOWED

    fun label(app: String): String =
        labels.getOrPut(app) {
            try {
                context.packageManager
                    .getApplicationLabel(context.packageManager.getApplicationInfo(app, 0))
                    .toString()
            } catch (_: android.content.pm.PackageManager.NameNotFoundException) {
                app.substringAfterLast('.')
            }
        }

    fun load(date: LocalDate): DayUsage =
        synchronized(ActivityStore.lock) {
            val now = System.currentTimeMillis()
            val access = hasAccess()
            val zone = ZoneId.systemDefault()
            val selectedFrom = date.atStartOfDay(zone).toInstant().toEpochMilli()
            if (access) archive()
            val retentionCutoff =
                LocalDate.now()
                    .minusDays(preferences.retention.toLong() - 1)
                    .atStartOfDay(zone)
                    .toInstant()
                    .toEpochMilli()
            store.prune(retentionCutoff)
            val from = maxOf(selectedFrom, retentionCutoff)
            val until = minOf(date.plusDays(1).atStartOfDay(zone).toInstant().toEpochMilli(), now)
            val sessions =
                if (from < until) store.read(from, until).filter { it.app !in preferences.excluded }
                else emptyList()
            val apps =
                sessions
                    .groupBy { it.app }
                    .map { (app, rows) ->
                        AppUsage(
                            app,
                            label(app),
                            rows.sumOf { it.milliseconds },
                            rows.size,
                            preferences.category(app),
                            preferences.limit(app),
                        )
                    }
                    .sortedByDescending { it.milliseconds }
            DayUsage(date, sessions, apps, access)
        }

    fun installedApps(): List<InstalledApp> =
        context.packageManager
            .queryIntentActivities(
                Intent(Intent.ACTION_MAIN).addCategory(Intent.CATEGORY_LAUNCHER),
                0,
            )
            .map {
                InstalledApp(
                    it.activityInfo.packageName,
                    it.loadLabel(context.packageManager).toString(),
                )
            }
            .distinctBy { it.app }
            .filter { it.app != context.packageName }
            .sortedBy { it.label.lowercase() }

    fun history(end: LocalDate, days: Int): List<Session> =
        synchronized(ActivityStore.lock) {
            val zone = ZoneId.systemDefault()
            val from =
                end.minusDays(days.toLong() - 1).atStartOfDay(zone).toInstant().toEpochMilli()
            val until =
                minOf(
                    end.plusDays(1).atStartOfDay(zone).toInstant().toEpochMilli(),
                    System.currentTimeMillis(),
                )
            if (from >= until) emptyList()
            else store.read(from, until).filter { it.app !in preferences.excluded }
        }

    fun blockHistory(): List<BlockEvent> =
        synchronized(ActivityStore.lock) {
            store.blocks(
                LocalDate.now()
                    .minusDays(6)
                    .atStartOfDay(ZoneId.systemDefault())
                    .toInstant()
                    .toEpochMilli()
            )
        }

    fun restoreActivity(rows: List<Session>) =
        synchronized(ActivityStore.lock) {
            store.restore(rows.filter { it.app !in preferences.excluded })
        }

    fun blockedCount(days: Int): Int =
        synchronized(ActivityStore.lock) {
            store.blockCount(
                LocalDate.now()
                    .minusDays(days.toLong() - 1)
                    .atStartOfDay(ZoneId.systemDefault())
                    .toInstant()
                    .toEpochMilli()
            )
        }

    fun recordBlock(app: String, reason: String) =
        synchronized(ActivityStore.lock) {
            if (app !in preferences.excluded)
                store.recordBlock(app, System.currentTimeMillis(), reason)
        }

    fun recordFocus(start: Long, end: Long, completed: Boolean) =
        synchronized(ActivityStore.lock) { store.recordFocus(start, end, completed) }

    fun completedFocusToday(): Int =
        synchronized(ActivityStore.lock) {
            store.focusCount(
                LocalDate.now().atStartOfDay(ZoneId.systemDefault()).toInstant().toEpochMilli()
            )
        }

    fun archive(force: Boolean = false) =
        synchronized(ActivityStore.lock) {
            val now = System.currentTimeMillis()
            val zone = ZoneId.systemDefault()
            SiteStore(context).use { sites ->
                sites.prune(
                    LocalDate.now()
                        .minusDays(preferences.retention.toLong() - 1)
                        .atStartOfDay(zone)
                        .toInstant()
                        .toEpochMilli()
                )
            }
            store.prune(
                LocalDate.now()
                    .minusDays(preferences.retention.toLong() - 1)
                    .atStartOfDay(zone)
                    .toInstant()
                    .toEpochMilli()
            )
            if (!hasAccess() || (!force && now - preferences.lastArchive < 15_000))
                return@synchronized
            val earliest =
                LocalDate.now()
                    .minusDays(minOf(6, preferences.retention - 1).toLong())
                    .atStartOfDay(zone)
                    .toInstant()
                    .toEpochMilli()
            val rewind =
                if (preferences.lastArchive == 0L) earliest
                else
                    Instant.ofEpochMilli(preferences.lastArchive)
                        .atZone(zone)
                        .toLocalDate()
                        .minusDays(1)
                        .atStartOfDay(zone)
                        .toInstant()
                        .toEpochMilli()
            if (!importRange(maxOf(earliest, rewind, preferences.since), now)) return@synchronized
            preferences.lastArchive = now
        }

    fun forceArchive() {
        archive(true)
    }

    private fun importRange(from: Long, until: Long): Boolean {
        if (from >= until) return false
        val observations = observations(from, until) ?: return false
        if (observations.isEmpty() || !hasAccess()) return false
        val sessions =
            UsageLedger.build(observations, from, until)
                .filter { it.app !in preferences.excluded }
                .map { it.copy(start = maxOf(it.start, preferences.appSince(it.app))) }
                .filter { it.end > it.start }
        val visible = UsageLedger.subtract(sessions, preferences.pausedSpans)
        store.replaceRange(from, until, visible)
        return true
    }

    private fun observations(from: Long, until: Long): List<Observation>? {
        val manager = context.getSystemService(UsageStatsManager::class.java)
        val events =
            manager.queryEvents((from - 86_400_000L).coerceAtLeast(0), until) ?: return null
        val observations = mutableListOf<Observation>()
        val event = UsageEvents.Event()
        while (events.hasNextEvent()) {
            events.getNextEvent(event)
            val kind =
                when (event.eventType) {
                    UsageEvents.Event.ACTIVITY_RESUMED -> Observation.Kind.RESUME
                    UsageEvents.Event.ACTIVITY_PAUSED,
                    UsageEvents.Event.ACTIVITY_STOPPED -> Observation.Kind.PAUSE
                    UsageEvents.Event.SCREEN_NON_INTERACTIVE -> Observation.Kind.SCREEN_OFF
                    UsageEvents.Event.KEYGUARD_SHOWN -> Observation.Kind.LOCK
                    UsageEvents.Event.DEVICE_SHUTDOWN -> Observation.Kind.SHUTDOWN
                    UsageEvents.Event.DEVICE_STARTUP -> Observation.Kind.STARTUP
                    else -> null
                }
            if (kind != null)
                observations +=
                    Observation(
                        event.timeStamp,
                        kind,
                        event.packageName ?: "",
                        event.className ?: "",
                    )
        }

        return observations
    }

    /**
     * Safety budgets read Android directly, independent of archive exclusions, pauses and deletion.
     */
    fun allowanceUsage(app: String, now: Long = System.currentTimeMillis()): Long? {
        if (!hasAccess()) return null
        val from = now - 86_400_000L
        val observations = observations(from, now) ?: return null
        val rows = UsageLedger.build(observations, from, now).filter { it.app == app }
        val masks = SiteStore(context).use { it.covered(app, from, now) }
        return UsageLedger.subtract(rows, masks).sumOf { it.milliseconds }
    }

    fun websites(end: LocalDate, days: Int): List<Session> {
        val zone = ZoneId.systemDefault()
        val from = end.minusDays(days.toLong() - 1).atStartOfDay(zone).toInstant().toEpochMilli()
        val until =
            minOf(
                end.plusDays(1).atStartOfDay(zone).toInstant().toEpochMilli(),
                System.currentTimeMillis(),
            )
        return SiteStore(context).use {
            it.prune(
                LocalDate.now()
                    .minusDays(preferences.retention.toLong() - 1)
                    .atStartOfDay(zone)
                    .toInstant()
                    .toEpochMilli()
            )
            if (from < until) it.read(from, until) else emptyList()
        }
    }

    fun exclude(app: String) =
        synchronized(ActivityStore.lock) {
            preferences.excluded = preferences.excluded + app
            store.exclude(app)
        }

    fun clear() =
        synchronized(ActivityStore.lock) {
            // Keep the deletion boundary so the next refresh cannot re-import deleted OS history.
            preferences.since = System.currentTimeMillis()
            preferences.focusEnd = 0
            preferences.focusPausedMs = 0
            preferences.lastArchive = 0
            store.clear()
            SiteStore(context).use { it.clear() }
        }

    override fun close() {
        store.close()
    }
}
