package `in`.venusapp.timelens.data

import android.content.Context
import `in`.venusapp.timelens.domain.BlockRule
import java.time.LocalDate
import java.time.ZoneId
import org.json.JSONArray
import org.json.JSONObject

class Preferences(context: Context) {
    private val appContext = context.applicationContext
    private val prefs = context.getSharedPreferences("timelens", Context.MODE_PRIVATE)

    init {
        // Freeze the initial boundary: a moving default would silently erase older archives.
        if (!prefs.contains("since")) {
            val initialBoundary =
                LocalDate.now()
                    .minusDays(6)
                    .atStartOfDay(ZoneId.systemDefault())
                    .toInstant()
                    .toEpochMilli()
            prefs.edit().putLong("since", initialBoundary).commit()
        }
        migrateBlockControls()
    }

    private fun migrateBlockControls() {
        if (!prefs.contains("blockModelV2")) {
            blockRules = blockRules.map { rule ->
                when (rule.mode) {
                    "always" -> rule.copy(mode = "strict")
                    "budget" -> rule.copy(mode = "comfort")
                    "focus",
                    "schedule" -> rule.copy(mode = "comfort", enabled = false)
                    else -> rule
                }
            }
            focusEnd = 0
            focusPausedMs = 0
            if (theme !in listOf("dark", "light")) theme = "dark"
            prefs.edit().putBoolean("blockModelV2", true).commit()
        }
    }

    var since: Long
        get() = prefs.getLong("since", 0)
        set(value) {
            prefs.edit().putLong("since", value).commit()
        }

    var retention: Int
        get() = prefs.getInt("retention", 90)
        set(value) {
            prefs.edit().putInt("retention", value.coerceIn(7, 365)).apply()
        }

    var budget: Int
        get() = prefs.getInt("budget", 240)
        set(value) {
            prefs.edit().putInt("budget", value.coerceIn(15, 1440)).apply()
        }

    var theme: String
        get() = prefs.getString("theme", "dark") ?: "dark"
        set(value) {
            prefs.edit().putString("theme", value).apply()
        }

    var focusEnd: Long
        get() = prefs.getLong("focusEnd", 0)
        set(value) {
            prefs.edit().putLong("focusEnd", value).apply()
        }

    var excluded: Set<String>
        get() = prefs.getStringSet("excluded", emptySet())!!.toSet()
        set(value) {
            prefs.edit().putStringSet("excluded", value.toSet()).commit()
        }

    fun category(app: String): String =
        prefs.getString("category.$app", null)
            ?: when {
                app.contains("launcher", true) ||
                    app == "com.android.settings" ||
                    app == appContext.packageName -> "Utilities"
                app.contains("chrome", true) ||
                    app.contains("firefox", true) ||
                    app.contains("browser", true) ||
                    app.contains("opera", true) -> "Browsing"
                app.contains("classroom", true) ||
                    app.contains("duolingo", true) ||
                    app.contains("kindle", true) ||
                    app.contains("coursera", true) -> "Education"
                app.contains("gmail", true) ||
                    app.contains("messaging", true) ||
                    app.contains("whatsapp", true) ||
                    app.contains("telegram", true) ||
                    app.contains("slack", true) ||
                    app.contains("teams", true) -> "Communication"
                else ->
                    runCatching {
                            when (appContext.packageManager.getApplicationInfo(app, 0).category) {
                                android.content.pm.ApplicationInfo.CATEGORY_GAME -> "Gaming"
                                android.content.pm.ApplicationInfo.CATEGORY_AUDIO,
                                android.content.pm.ApplicationInfo.CATEGORY_VIDEO -> "Entertainment"
                                android.content.pm.ApplicationInfo.CATEGORY_SOCIAL -> "Social"
                                android.content.pm.ApplicationInfo.CATEGORY_PRODUCTIVITY -> "Work"
                                android.content.pm.ApplicationInfo.CATEGORY_NEWS -> "News"
                                android.content.pm.ApplicationInfo.CATEGORY_MAPS -> "Utilities"
                                else -> "Other"
                            }
                        }
                        .getOrDefault("Other")
            }

    fun setCategory(app: String, value: String) {
        prefs.edit().putString("category.$app", value).apply()
    }

    fun limit(app: String) = prefs.getInt("limit.$app", 0)

    fun setLimit(app: String, value: Int) {
        prefs.edit().putInt("limit.$app", value.coerceIn(0, 1440)).apply()
    }

    fun appSince(app: String): Long = prefs.getLong("since.$app", 0)

    fun restore(app: String, now: Long = System.currentTimeMillis()) {
        // Do not backfill the time during which this app was excluded.
        prefs.edit().putLong("since.$app", now).putStringSet("excluded", excluded - app).commit()
    }

    fun focusRemaining(now: Long): Long = (focusEnd - now).coerceAtLeast(0)

    var accent: String
        get() = prefs.getString("accent", "acid") ?: "acid"
        set(value) {
            prefs.edit().putString("accent", value).apply()
        }

    var grayIcons: Boolean
        get() = prefs.getBoolean("grayIcons", true)
        set(value) {
            prefs.edit().putBoolean("grayIcons", value).apply()
        }

    var websiteTracking: Boolean
        get() = prefs.getBoolean("websiteTracking", false)
        set(value) {
            prefs.edit().putBoolean("websiteTracking", value).commit()
        }

    var compact: Boolean
        get() = prefs.getBoolean("compact", false)
        set(value) {
            prefs.edit().putBoolean("compact", value).apply()
        }

    var use24Hour: Boolean
        get() = prefs.getBoolean("use24Hour", true)
        set(value) {
            prefs.edit().putBoolean("use24Hour", value).apply()
        }

    var showSeconds: Boolean
        get() = prefs.getBoolean("showSeconds", false)
        set(value) {
            prefs.edit().putBoolean("showSeconds", value).apply()
        }

    var backgroundArchive: Boolean
        get() = prefs.getBoolean("backgroundArchive", true)
        set(value) {
            prefs.edit().putBoolean("backgroundArchive", value).commit()
        }

    var lastArchive: Long
        get() = prefs.getLong("lastArchive", 0)
        set(value) {
            prefs.edit().putLong("lastArchive", value).apply()
        }

    var pauseTrackingUntil: Long
        get() = prefs.getLong("pauseTrackingUntil", 0)
        set(value) {
            prefs.edit().putLong("pauseTrackingUntil", value).commit()
        }

    var pausedSpans: List<Pair<Long, Long>>
        get() = runCatching {
            val array = JSONArray(prefs.getString("pausedSpans", "[]"))
            (0 until array.length()).map {
                val span = array.getJSONArray(it)
                span.getLong(0) to span.getLong(1)
            }
        }
            .getOrDefault(emptyList())
        set(value) {
            prefs
                .edit()
                .putString(
                    "pausedSpans",
                    JSONArray()
                        .apply { value.forEach { put(JSONArray().put(it.first).put(it.second)) } }
                        .toString(),
                )
                .commit()
        }

    var focusPausedMs: Long
        get() = prefs.getLong("focusPausedMs", 0)
        set(value) {
            prefs.edit().putLong("focusPausedMs", value).commit()
        }

    var focusStarted: Long
        get() = prefs.getLong("focusStarted", 0)
        set(value) {
            prefs.edit().putLong("focusStarted", value).commit()
        }

    var focusMinutes: Int
        get() = prefs.getInt("focusMinutes", 25)
        set(value) {
            prefs.edit().putInt("focusMinutes", value.coerceIn(1, 240)).apply()
        }

    var blocksEnabled: Boolean
        get() = prefs.getBoolean("blocksEnabled", false)
        set(value) {
            prefs.edit().putBoolean("blocksEnabled", value).commit()
        }

    var blockPauseUntil: Long
        get() = prefs.getLong("blockPauseUntil", 0)
        set(value) {
            prefs.edit().putLong("blockPauseUntil", value).commit()
        }

    var blockTitle: String
        get() = prefs.getString("blockTitle", "Protect your attention") ?: "Protect your attention"
        set(value) {
            prefs.edit().putString("blockTitle", value.take(100)).apply()
        }

    var blockMessage: String
        get() =
            prefs.getString(
                "blockMessage",
                "{app} can wait. Take a breath and return to what matters.",
            ) ?: ""
        set(value) {
            prefs.edit().putString("blockMessage", value.take(500)).apply()
        }

    var pinHash: String
        get() = prefs.getString("pinHash", "") ?: ""
        set(value) {
            prefs.edit().putString("pinHash", value).commit()
        }

    var pinFailures: Int
        get() = prefs.getInt("pinFailures", 0)
        set(value) {
            prefs.edit().putInt("pinFailures", value).commit()
        }

    var pinRetryAt: Long
        get() = prefs.getLong("pinRetryAt", 0)
        set(value) {
            prefs.edit().putLong("pinRetryAt", value).commit()
        }

    var cards: List<String>
        get() =
            prefs
                .getString("cards", "hourly,categories,week,apps")!!
                .split(',')
                .filter { it in listOf("hourly", "categories", "week", "apps") }
                .distinct()
        set(value) {
            prefs.edit().putString("cards", value.joinToString(",")).apply()
        }

    var productiveCategories: Set<String>
        get() = prefs.getStringSet("productive", setOf("Work", "Education"))!!.toSet()
        set(value) {
            prefs.edit().putStringSet("productive", value.toSet()).apply()
        }

    var blockRules: List<BlockRule>
        get() = runCatching {
            val array = JSONArray(prefs.getString("blockRules", "[]"))
            (0 until array.length()).map { index ->
                val item = array.getJSONObject(index)
                val days = item.optJSONArray("days") ?: JSONArray()
                BlockRule(
                    item.getString("app"),
                    item.optString("mode", "focus"),
                    item.optBoolean("enabled", true),
                    item.optInt("start", 540),
                    item.optInt("end", 1020),
                    (0 until days.length()).map { days.getInt(it) }.toSet(),
                    item.optInt("allowance", limit(item.getString("app")).takeIf { it > 0 } ?: 30),
                )
            }
        }
            .getOrDefault(emptyList())
        set(value) {
            prefs
                .edit()
                .putString(
                    "blockRules",
                    JSONArray()
                        .apply {
                            value.forEach { rule ->
                                put(
                                    JSONObject()
                                        .put("app", rule.app)
                                        .put("mode", rule.mode)
                                        .put("enabled", rule.enabled)
                                        .put("start", rule.startMinute)
                                        .put("end", rule.endMinute)
                                        .put("days", JSONArray(rule.days.sorted()))
                                        .put("allowance", rule.allowanceMinutes)
                                )
                            }
                        }
                        .toString(),
                )
                .commit()
        }

    fun updateRule(rule: BlockRule) {
        blockRules = blockRules.filterNot { it.app == rule.app } + rule
    }

    fun pauseTracking(minutes: Int, now: Long = System.currentTimeMillis()) {
        val end = now + minutes * 60_000L
        pausedSpans = (pausedSpans.filter { it.second > now - 366L * 86_400_000 } + (now to end))
        pauseTrackingUntil = end
    }

    fun resumeTracking(now: Long = System.currentTimeMillis()) {
        pausedSpans = pausedSpans.map { (from, until) ->
            from to if (until > now) maxOf(from, now) else until
        }
        pauseTrackingUntil = 0
    }

    companion object {
        val categories =
            listOf(
                "Work",
                "Education",
                "Communication",
                "Social",
                "Entertainment",
                "Gaming",
                "Utilities",
                "Browsing",
                "Development",
                "Design",
                "Documents",
                "Finance",
                "Health",
                "News",
                "Other",
            )
    }
}
