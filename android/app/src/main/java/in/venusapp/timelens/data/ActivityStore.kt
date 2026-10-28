package `in`.venusapp.timelens.data

import android.content.ContentValues
import android.content.Context
import android.database.sqlite.SQLiteDatabase
import android.database.sqlite.SQLiteOpenHelper
import `in`.venusapp.timelens.domain.Session

class ActivityStore(context: Context) : SQLiteOpenHelper(context, "activity.db", null, 1) {
    override fun onCreate(db: SQLiteDatabase) {
        db.execSQL("CREATE TABLE sessions(app TEXT NOT NULL, start INTEGER NOT NULL, end INTEGER NOT NULL CHECK(end > start), PRIMARY KEY(app,start))")
        db.execSQL("CREATE INDEX sessions_end ON sessions(end)")
    }
    override fun onUpgrade(db: SQLiteDatabase, oldVersion: Int, newVersion: Int) = Unit
    fun replaceRange(from: Long, until: Long, sessions: List<Session>) {
        val db = writableDatabase
        db.beginTransaction()
        try {
            db.delete("sessions", "start >= ? AND start < ?", arrayOf(from.toString(), until.toString()))
            sessions.forEach { session ->
                val row = ContentValues().apply { put("app", session.app); put("start", session.start); put("end", session.end) }
                db.insertWithOnConflict("sessions", null, row, SQLiteDatabase.CONFLICT_REPLACE)
            }
            db.setTransactionSuccessful()
        } finally { db.endTransaction() }
    }
    fun read(from: Long, until: Long): List<Session> {
        val rows = mutableListOf<Session>()
        readableDatabase.rawQuery("SELECT app,start,end FROM sessions WHERE end > ? AND start < ? ORDER BY start", arrayOf(from.toString(), until.toString())).use { cursor ->
            while (cursor.moveToNext()) rows += Session(cursor.getString(0), maxOf(from, cursor.getLong(1)), minOf(until, cursor.getLong(2)))
        }
        return rows
    }
    fun prune(cutoff: Long) { writableDatabase.delete("sessions", "end <= ?", arrayOf(cutoff.toString())) }
    fun exclude(app: String) { writableDatabase.delete("sessions", "app = ?", arrayOf(app)) }
    fun clear() { writableDatabase.delete("sessions", null, null) }
}
