package `in`.venusapp.timelens.data

import android.content.Context
import java.time.LocalDate
import java.time.ZoneId

class Preferences(context: Context) {
    private val prefs = context.getSharedPreferences("timelens", Context.MODE_PRIVATE)
    init {
        // Freeze the initial boundary: a moving default would silently erase older archives.
        if (!prefs.contains("since")) {
            val initialBoundary = LocalDate.now().minusDays(6).atStartOfDay(ZoneId.systemDefault()).toInstant().toEpochMilli()
            prefs.edit().putLong("since", initialBoundary).commit()
        }
    }
    var since: Long
        get() = prefs.getLong("since", 0)
        set(value) { prefs.edit().putLong("since", value).commit() }
    var retention: Int
        get() = prefs.getInt("retention", 90)
        set(value) { prefs.edit().putInt("retention", value.coerceIn(7, 365)).apply() }
    var budget: Int
        get() = prefs.getInt("budget", 240)
        set(value) { prefs.edit().putInt("budget", value.coerceIn(15, 1440)).apply() }
    var theme: String
        get() = prefs.getString("theme", "system") ?: "system"
        set(value) { prefs.edit().putString("theme", value).apply() }
    var focusEnd: Long
        get() = prefs.getLong("focusEnd", 0)
        set(value) { prefs.edit().putLong("focusEnd", value).apply() }
    var excluded: Set<String>
        get() = prefs.getStringSet("excluded", emptySet())!!.toSet()
        set(value) { prefs.edit().putStringSet("excluded", value.toSet()).commit() }
    fun category(app: String) = prefs.getString("category.$app", "Other") ?: "Other"
    fun setCategory(app: String, value: String) { prefs.edit().putString("category.$app", value).apply() }
    fun limit(app: String) = prefs.getInt("limit.$app", 0)
    fun setLimit(app: String, value: Int) { prefs.edit().putInt("limit.$app", value.coerceIn(0, 1440)).apply() }
    fun appSince(app: String): Long = prefs.getLong("since.$app", 0)
    fun restore(app: String, now: Long = System.currentTimeMillis()) {
        // Do not backfill the time during which this app was excluded.
        prefs.edit().putLong("since.$app", now).putStringSet("excluded", excluded - app).commit()
    }
    fun focusRemaining(now: Long): Long = (focusEnd - now).coerceAtLeast(0)
    companion object {
        val categories = listOf("Work", "Education", "Communication", "Social", "Entertainment", "Gaming", "Utilities", "Other")
    }
}
