package `in`.venusapp.timelens.data

import `in`.venusapp.timelens.domain.Session
import java.time.Instant
import org.json.JSONObject

object Import {
    const val MAX_BYTES = 10 * 1024 * 1024

    fun read(input: java.io.InputStream): String {
        val output = java.io.ByteArrayOutputStream()
        val buffer = ByteArray(8192)
        while (true) {
            val count = input.read(buffer)
            if (count < 0) break
            require(output.size() + count <= MAX_BYTES) { "The file is larger than 10 MB." }
            output.write(buffer, 0, count)
        }
        return output.toString("UTF-8")
    }

    fun parse(text: String, now: Long = System.currentTimeMillis()): List<Session> {
        require(text.toByteArray(Charsets.UTF_8).size <= MAX_BYTES) {
            "The file is larger than 10 MB."
        }
        val result = mutableListOf<Session>()
        fun readDay(day: JSONObject) {
            require(day.getString("schema") == "timelens.android.day.v1") {
                "This is not a supported Android activity export."
            }
            val sessions = day.getJSONArray("sessions")
            require(result.size + sessions.length() <= 50_000) {
                "Import at most 50,000 intervals at a time."
            }
            for (index in 0 until sessions.length()) {
                val row = sessions.getJSONObject(index)
                val app = row.getString("package")
                require(app.matches(Regex("[A-Za-z][A-Za-z0-9_.]{0,254}"))) {
                    "The file contains an invalid package name."
                }
                val start = Instant.parse(row.getString("start")).toEpochMilli()
                val end = Instant.parse(row.getString("end")).toEpochMilli()
                require(start >= 0 && end > start && end <= now + 300_000) {
                    "The file contains invalid or future activity."
                }
                result += Session(app, start, end)
            }
        }
        val root = JSONObject(text)
        if (root.optString("schema") == "timelens.android.range.v1") {
            val days = root.getJSONArray("days")
            require(days.length() <= 366) { "Too many days in this file." }
            for (i in 0 until days.length()) readDay(days.getJSONObject(i))
        } else readDay(root)
        val rows = result.distinct().sortedBy { it.start }
        require(rows.isNotEmpty()) { "The file contains no activity." }
        require(rows.zipWithNext().all { (a, b) -> a.end <= b.start }) {
            "Overlapping activity cannot be restored as exclusive foreground time."
        }
        return rows
    }
}
