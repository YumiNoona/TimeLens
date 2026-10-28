package `in`.venusapp.timelens.platform

import android.Manifest
import android.app.*
import android.app.job.*
import android.content.*
import android.content.pm.PackageManager
import android.os.Build
import `in`.venusapp.timelens.MainActivity
import `in`.venusapp.timelens.R
import `in`.venusapp.timelens.data.Preferences
import `in`.venusapp.timelens.data.UsageRepository
import kotlinx.coroutines.*

object BackgroundTasks {
    const val ARCHIVE_JOB = 80401
    private const val CHANNEL = "timelens_focus"

    fun setup(context: Context) {
        val prefs = Preferences(context)
        val jobs = context.getSystemService(JobScheduler::class.java)
        if (prefs.backgroundArchive) {
            if (jobs.getPendingJob(ARCHIVE_JOB) == null)
                jobs.schedule(
                    JobInfo.Builder(
                            ARCHIVE_JOB,
                            ComponentName(context, ArchiveJobService::class.java),
                        )
                        .setPeriodic(15 * 60_000L)
                        .setPersisted(true)
                        .build()
                )
        } else jobs.cancel(ARCHIVE_JOB)
        context
            .getSystemService(NotificationManager::class.java)
            .createNotificationChannel(
                NotificationChannel(
                    CHANNEL,
                    "Focus sessions",
                    NotificationManager.IMPORTANCE_DEFAULT,
                )
            )
        scheduleFocus(context)
    }

    private fun alarmIntent(context: Context) =
        PendingIntent.getBroadcast(
            context,
            80402,
            Intent(context, FocusAlarmReceiver::class.java),
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE,
        )

    fun scheduleFocus(context: Context) {
        val manager = context.getSystemService(AlarmManager::class.java)
        manager.cancel(alarmIntent(context))
        val deadline = Preferences(context).focusEnd
        if (deadline <= 0) return
        val at = maxOf(System.currentTimeMillis() + 1000, deadline)
        try {
            if (Build.VERSION.SDK_INT < 31 || manager.canScheduleExactAlarms())
                manager.setExactAndAllowWhileIdle(AlarmManager.RTC_WAKEUP, at, alarmIntent(context))
            else manager.setAndAllowWhileIdle(AlarmManager.RTC_WAKEUP, at, alarmIntent(context))
        } catch (_: SecurityException) {
            manager.setAndAllowWhileIdle(AlarmManager.RTC_WAKEUP, at, alarmIntent(context))
        }
    }

    fun notify(context: Context, title: String, message: String, id: Int = 80402) {
        if (
            Build.VERSION.SDK_INT >= 33 &&
                context.checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) !=
                    PackageManager.PERMISSION_GRANTED
        )
            return
        val open =
            PendingIntent.getActivity(
                context,
                id,
                Intent(context, MainActivity::class.java),
                PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE,
            )
        runCatching {
            context
                .getSystemService(NotificationManager::class.java)
                .notify(
                    id,
                    Notification.Builder(context, CHANNEL)
                        .setSmallIcon(R.drawable.ic_notification)
                        .setContentTitle(title)
                        .setContentText(message)
                        .setStyle(Notification.BigTextStyle().bigText(message))
                        .setContentIntent(open)
                        .setAutoCancel(true)
                        .build(),
                )
        }
    }

    fun finishFocus(context: Context) =
        synchronized(`in`.venusapp.timelens.data.ActivityStore.lock) {
            val prefs = Preferences(context)
            val end = prefs.focusEnd
            if (end <= 0 || end > System.currentTimeMillis()) return@synchronized
            val started = prefs.focusStarted
            UsageRepository(context, prefs).use {
                if (started > 0) it.recordFocus(started, end, true)
            }
            prefs.focusEnd = 0
            prefs.focusPausedMs = 0
            prefs.focusStarted = 0
            notify(
                context,
                "Focus session complete",
                "Well done. Take a moment to stretch before your next session.",
            )
        }
}

class ArchiveJobService : JobService() {
    private var task: Job? = null
    private var stopped = false

    override fun onStartJob(params: JobParameters): Boolean {
        stopped = false
        task =
            CoroutineScope(Dispatchers.IO).launch {
                var retry = false
                try {
                    val prefs = Preferences(applicationContext)
                    if (prefs.backgroundArchive)
                        UsageRepository(applicationContext, prefs).use { it.forceArchive() }
                    BackgroundTasks.finishFocus(applicationContext)
                } catch (cancelled: CancellationException) {
                    throw cancelled
                } catch (_: Exception) {
                    retry = true
                } finally {
                    withContext(NonCancellable + Dispatchers.Main) {
                        if (!stopped) jobFinished(params, retry)
                    }
                }
            }
        return true
    }

    override fun onStopJob(params: JobParameters): Boolean {
        stopped = true
        task?.cancel()
        return true
    }
}

class FocusAlarmReceiver : BroadcastReceiver() {
    override fun onReceive(context: Context, intent: Intent) {
        val pending = goAsync()
        CoroutineScope(Dispatchers.IO).launch {
            try {
                BackgroundTasks.finishFocus(context)
            } catch (_: Exception) {
                // A later archive or foreground refresh retries an unfinished deadline.
            } finally {
                pending.finish()
            }
        }
    }
}

class BootReceiver : BroadcastReceiver() {
    override fun onReceive(context: Context, intent: Intent) {
        if (
            intent.action in
                listOf(
                    Intent.ACTION_BOOT_COMPLETED,
                    Intent.ACTION_MY_PACKAGE_REPLACED,
                    AlarmManager.ACTION_SCHEDULE_EXACT_ALARM_PERMISSION_STATE_CHANGED,
                )
        )
            BackgroundTasks.setup(context)
    }
}
