package `in`.venusapp.timelens.data

import org.json.JSONArray
import org.json.JSONObject
import java.time.Instant
import java.time.ZoneId

object Export {
    fun json(day: DayUsage): String = JSONObject().apply {
        put("schema", "timelens.android.day.v1")
        put("version", "8.4.0")
        put("device", "android")
        put("date", day.date.toString())
        put("timezone", ZoneId.systemDefault().id)
        put("exportedAt", Instant.now().toString())
        put("sessions", JSONArray().apply {
            day.sessions.forEach { row -> put(JSONObject().apply {
                put("package", row.app)
                put("start", Instant.ofEpochMilli(row.start).toString())
                put("end", Instant.ofEpochMilli(row.end).toString())
                put("durationMilliseconds", row.milliseconds)
            }) }
        })
    }.toString(2)
    fun csv(day: DayUsage): String = buildString {
        append("package,start,end,durationMilliseconds\r\n")
        day.sessions.forEach { row ->
            append(listOf(row.app, Instant.ofEpochMilli(row.start).toString(), Instant.ofEpochMilli(row.end).toString(), row.milliseconds.toString()).joinToString(",") { cell(it) })
            append("\r\n")
        }
    }
    private fun cell(value: String): String {
        val safe = if (value.firstOrNull() in listOf('=', '+', '-', '@', '\t', '\r')) "'$value" else value
        return "\"${safe.replace("\"", "\"\"")}\""
    }
}
