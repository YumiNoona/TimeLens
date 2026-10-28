package `in`.venusapp.timelens.data

import android.content.ContentValues
import android.content.Context
import android.database.sqlite.SQLiteDatabase
import android.database.sqlite.SQLiteOpenHelper
import `in`.venusapp.timelens.domain.Session

class ActivityStore(context: Context) : SQLiteOpenHelper(context, "activity.db", null, 2) {
    init {
        setWriteAheadLoggingEnabled(true)
    }

    override fun onConfigure(db: SQLiteDatabase) {
        db.rawQuery("PRAGMA busy_timeout=5000", null).use { it.moveToFirst() }
    }

    override fun onCreate(db: SQLiteDatabase) {
        db.execSQL(
            "CREATE TABLE sessions(app TEXT NOT NULL, start INTEGER NOT NULL, end INTEGER NOT NULL CHECK(end > start), PRIMARY KEY(app,start))"
        )
        db.execSQL("CREATE INDEX sessions_end ON sessions(end)")
        createAdditionalTables(db)
    }

    override fun onUpgrade(db: SQLiteDatabase, oldVersion: Int, newVersion: Int) {
        if (oldVersion < 2) createAdditionalTables(db)
    }

    private fun createAdditionalTables(db: SQLiteDatabase) {
        db.execSQL(
            "CREATE TABLE IF NOT EXISTS block_events(id INTEGER PRIMARY KEY, app TEXT NOT NULL, at INTEGER NOT NULL, reason TEXT NOT NULL)"
        )
        db.execSQL("CREATE INDEX IF NOT EXISTS block_events_at ON block_events(at)")
        db.execSQL(
            "CREATE TABLE IF NOT EXISTS focus_sessions(id INTEGER PRIMARY KEY, start INTEGER NOT NULL, end INTEGER NOT NULL, completed INTEGER NOT NULL)"
        )
    }

    fun replaceRange(from: Long, until: Long, sessions: List<Session>) {
        require(
            until > from &&
                sessions.all { it.start >= from && it.end <= until && it.end > it.start }
        )
        val db = writableDatabase
        db.beginTransaction()
        try {
            val edges = mutableListOf<Session>()
            db.rawQuery(
                    "SELECT app,start,end FROM sessions WHERE end > ? AND start < ?",
                    arrayOf(from.toString(), until.toString()),
                )
                .use { cursor ->
                    while (cursor.moveToNext()) {
                        val app = cursor.getString(0)
                        val start = cursor.getLong(1)
                        val end = cursor.getLong(2)
                        if (start < from) edges += Session(app, start, from)
                        if (end > until) edges += Session(app, until, end)
                    }
                }
            db.delete(
                "sessions",
                "end > ? AND start < ?",
                arrayOf(from.toString(), until.toString()),
            )
            (edges + sessions).forEach { session ->
                val row =
                    ContentValues().apply {
                        put("app", session.app)
                        put("start", session.start)
                        put("end", session.end)
                    }
                db.insertWithOnConflict("sessions", null, row, SQLiteDatabase.CONFLICT_REPLACE)
            }
            db.setTransactionSuccessful()
        } finally {
            db.endTransaction()
        }
    }

    fun read(from: Long, until: Long): List<Session> {
        val rows = mutableListOf<Session>()
        readableDatabase
            .rawQuery(
                "SELECT app,start,end FROM sessions WHERE end > ? AND start < ? ORDER BY start",
                arrayOf(from.toString(), until.toString()),
            )
            .use { cursor ->
                while (cursor.moveToNext()) rows +=
                    Session(
                        cursor.getString(0),
                        maxOf(from, cursor.getLong(1)),
                        minOf(until, cursor.getLong(2)),
                    )
            }
        return rows
    }

    fun prune(cutoff: Long) {
        writableDatabase.delete("sessions", "end <= ?", arrayOf(cutoff.toString()))
        writableDatabase.delete("block_events", "at < ?", arrayOf(cutoff.toString()))
        writableDatabase.delete("focus_sessions", "end < ?", arrayOf(cutoff.toString()))
    }

    fun exclude(app: String) {
        writableDatabase.delete("sessions", "app = ?", arrayOf(app))
        writableDatabase.delete("block_events", "app = ?", arrayOf(app))
    }

    fun clear() {
        writableDatabase.delete("sessions", null, null)
        writableDatabase.delete("block_events", null, null)
        writableDatabase.delete("focus_sessions", null, null)
    }

    fun restore(rows: List<Session>) {
        writableDatabase.beginTransaction()
        try {
            rows.forEach { replaceRange(it.start, it.end, listOf(it)) }
            writableDatabase.setTransactionSuccessful()
        } finally {
            writableDatabase.endTransaction()
        }
    }

    fun recordBlock(app: String, at: Long, reason: String) {
        writableDatabase.insert(
            "block_events",
            null,
            ContentValues().apply {
                put("app", app)
                put("at", at)
                put("reason", reason)
            },
        )
    }

    fun blocks(from: Long): List<BlockEvent> = buildList {
        readableDatabase
            .rawQuery(
                "SELECT app,at,reason FROM block_events WHERE at >= ? ORDER BY at DESC LIMIT 200",
                arrayOf(from.toString()),
            )
            .use {
                while (it.moveToNext()) add(
                    BlockEvent(it.getString(0), it.getLong(1), it.getString(2))
                )
            }
    }

    fun blockCount(from: Long): Int =
        readableDatabase
            .rawQuery("SELECT COUNT(*) FROM block_events WHERE at>=?", arrayOf(from.toString()))
            .use {
                it.moveToFirst()
                it.getInt(0)
            }

    fun recordFocus(start: Long, end: Long, completed: Boolean) {
        writableDatabase.insert(
            "focus_sessions",
            null,
            ContentValues().apply {
                put("start", start)
                put("end", end)
                put("completed", if (completed) 1 else 0)
            },
        )
    }

    fun focusCount(from: Long): Int =
        readableDatabase
            .rawQuery(
                "SELECT COUNT(*) FROM focus_sessions WHERE completed=1 AND end>=?",
                arrayOf(from.toString()),
            )
            .use {
                it.moveToFirst()
                it.getInt(0)
            }

    companion object {
        val lock = Any()
    }
}

data class BlockEvent(val app: String, val at: Long, val reason: String)
