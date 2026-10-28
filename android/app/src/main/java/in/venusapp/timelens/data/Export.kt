package `in`.venusapp.timelens.data

import `in`.venusapp.timelens.BuildConfig
import java.time.Instant
import java.time.ZoneId
import org.json.JSONArray
import org.json.JSONObject

object Export {
    fun json(day: DayUsage): String =
        JSONObject()
            .apply {
                put("schema", "timelens.android.day.v1")
                put("version", BuildConfig.VERSION_NAME)
                put("device", "android")
                put("date", day.date.toString())
                put("timezone", ZoneId.systemDefault().id)
                put("exportedAt", Instant.now().toString())
                put(
                    "sessions",
                    JSONArray().apply {
                        day.sessions.forEach { row ->
                            put(
                                JSONObject().apply {
                                    put("package", row.app)
                                    put("start", Instant.ofEpochMilli(row.start).toString())
                                    put("end", Instant.ofEpochMilli(row.end).toString())
                                    put("durationMilliseconds", row.milliseconds)
                                }
                            )
                        }
                    },
                )
            }
            .toString(2)

    fun jsonRange(days: List<DayUsage>): String =
        if (days.size == 1) json(days.first())
        else
            JSONObject()
                .put("schema", "timelens.android.range.v1")
                .put("version", BuildConfig.VERSION_NAME)
                .put("days", JSONArray().apply { days.forEach { put(JSONObject(json(it))) } })
                .toString(2)

    fun csvRange(days: List<DayUsage>): String =
        if (days.size == 1) csv(days.first())
        else
            buildString {
                append("date,package,start,end,durationMilliseconds\r\n")
                days.forEach { day ->
                    csv(day)
                        .lineSequence()
                        .drop(1)
                        .filter { it.isNotBlank() }
                        .forEach { append("${day.date},$it\r\n") }
                }
            }

    fun csv(day: DayUsage): String = buildString {
        append("package,start,end,durationMilliseconds\r\n")
        day.sessions.forEach { row ->
            append(
                listOf(
                        row.app,
                        Instant.ofEpochMilli(row.start).toString(),
                        Instant.ofEpochMilli(row.end).toString(),
                        row.milliseconds.toString(),
                    )
                    .joinToString(",") { cell(it) }
            )
            append("\r\n")
        }
    }

    private fun cell(value: String): String {
        val safe =
            if (value.firstOrNull() in listOf('=', '+', '-', '@', '\t', '\r')) "'$value" else value
        return "\"${safe.replace("\"", "\"\"")}\""
    }
}
