package `in`.venusapp.timelens.data

import android.content.ContentValues
import android.content.Context
import android.database.sqlite.SQLiteDatabase
import android.database.sqlite.SQLiteOpenHelper
import `in`.venusapp.timelens.domain.Session
import `in`.venusapp.timelens.domain.WebAddress

/** Browser observations live separately from Android's app UsageEvents. */
class SiteStore(context: Context) : SQLiteOpenHelper(context, "web_history.db", null, 1) {
    override fun onCreate(db: SQLiteDatabase) {
        db.execSQL(
            "CREATE TABLE sites(host TEXT NOT NULL,start INTEGER NOT NULL,end INTEGER NOT NULL,PRIMARY KEY(host,start))"
        )
        db.execSQL("CREATE INDEX sites_end ON sites(end)")
        db.execSQL(
            "CREATE TABLE covered(app TEXT NOT NULL,start INTEGER NOT NULL,end INTEGER NOT NULL,PRIMARY KEY(app,start))"
        )
    }

    override fun onUpgrade(db: SQLiteDatabase, oldVersion: Int, newVersion: Int) = Unit

    fun observe(host: String, from: Long, until: Long) =
        synchronized(lock) {
            require(WebAddress.host(host) == host && until > from && until - from <= 5000)
            append("sites", "host", host, from, until)
        }

    fun cover(app: String, from: Long, until: Long) =
        synchronized(lock) {
            if (until > from) append("covered", "app", app, from, until)
        }

    private fun append(table: String, column: String, target: String, from: Long, until: Long) {
        writableDatabase.beginTransaction()
        try {
            val start =
                readableDatabase
                    .rawQuery(
                        "SELECT start FROM $table WHERE $column=? AND end=? LIMIT 1",
                        arrayOf(target, from.toString()),
                    )
                    .use {
                        if (it.moveToFirst()) it.getLong(0) else from
                    }
            writableDatabase.insertWithOnConflict(
                table,
                null,
                ContentValues().apply {
                    put(column, target)
                    put("start", start)
                    put("end", until)
                },
                SQLiteDatabase.CONFLICT_REPLACE,
            )
            writableDatabase.setTransactionSuccessful()
        } finally {
            writableDatabase.endTransaction()
        }
    }

    fun read(from: Long, until: Long): List<Session> =
        synchronized(lock) {
            buildList {
                readableDatabase
                    .rawQuery(
                        "SELECT host,start,end FROM sites WHERE end>? AND start<? ORDER BY start",
                        arrayOf(from.toString(), until.toString()),
                    )
                    .use {
                        while (it.moveToNext()) add(
                            Session(
                                it.getString(0),
                                maxOf(from, it.getLong(1)),
                                minOf(until, it.getLong(2)),
                            )
                        )
                    }
            }
        }

    fun covered(app: String, from: Long, until: Long): List<Pair<Long, Long>> =
        synchronized(lock) {
            buildList {
                readableDatabase
                    .rawQuery(
                        "SELECT start,end FROM covered WHERE app=? AND end>? AND start<?",
                        arrayOf(app, from.toString(), until.toString()),
                    )
                    .use {
                        while (it.moveToNext()) add(
                            maxOf(from, it.getLong(0)) to minOf(until, it.getLong(1))
                        )
                    }
            }
        }

    fun prune(cutoff: Long) =
        synchronized(lock) {
            writableDatabase.delete("sites", "end<=?", arrayOf(cutoff.toString()))
            writableDatabase.delete("covered", "end<=?", arrayOf(cutoff.toString()))
        }

    fun clear() = synchronized(lock) { writableDatabase.delete("sites", null, null) }

    fun clearAll() =
        synchronized(lock) {
            writableDatabase.beginTransaction()
            try {
                writableDatabase.delete("sites", null, null)
                writableDatabase.delete("covered", null, null)
                writableDatabase.setTransactionSuccessful()
            } finally {
                writableDatabase.endTransaction()
            }
        }

    companion object {
        private val lock = Any()
    }
}
