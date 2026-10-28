package `in`.venusapp.timelens.data

import android.app.UiAutomation
import android.os.ParcelFileDescriptor
import android.os.SystemClock
import android.provider.Settings
import androidx.test.platform.app.InstrumentationRegistry
import `in`.venusapp.timelens.domain.BlockRule
import `in`.venusapp.timelens.platform.*
import org.junit.Assert.*
import org.junit.Test

/**
 * Run on a dedicated emulator: temporarily enables the actual blocking service via the test shell.
 */
class PlatformIntegrationTest {
    @Test
    fun revokingUsageAccessPreservesSavedHistory() {
        val automation =
            instrumentation.getUiAutomation(UiAutomation.FLAG_DONT_SUPPRESS_ACCESSIBILITY_SERVICES)
        fun shell(command: String) =
            ParcelFileDescriptor.AutoCloseInputStream(automation.executeShellCommand(command))
                .bufferedReader()
                .use { it.readText() }
        val prefs = Preferences(context)
        val hadAccess = UsageRepository(context, prefs).use { it.hasAccess() }
        val midnight =
            java.time.LocalDate.now()
                .atStartOfDay(java.time.ZoneId.systemDefault())
                .toInstant()
                .toEpochMilli()
        val row =
            `in`.venusapp.timelens.domain.Session("saved.app", midnight + 1000, midnight + 2000)
        try {
            ActivityStore(context).use { it.restore(listOf(row)) }
            shell("appops set ${context.packageName} GET_USAGE_STATS ignore")
            UsageRepository(context, prefs).use { repo ->
                assertFalse(repo.hasAccess())
                assertTrue(repo.load(java.time.LocalDate.now()).sessions.contains(row))
            }
        } finally {
            shell(
                "appops set ${context.packageName} GET_USAGE_STATS ${if(hadAccess) "allow" else "default"}"
            )
        }
    }

    private val instrumentation = InstrumentationRegistry.getInstrumentation()
    private val context = instrumentation.targetContext

    private fun await(message: String, predicate: () -> Boolean) {
        val deadline = SystemClock.elapsedRealtime() + 15_000
        while (SystemClock.elapsedRealtime() < deadline) {
            if (predicate()) return
            SystemClock.sleep(100)
        }
        assertTrue(message, predicate())
    }

    @Test
    fun scheduledAlarmDeliversNotificationWithoutVisibleActivity() {
        val automation =
            instrumentation.getUiAutomation(UiAutomation.FLAG_DONT_SUPPRESS_ACCESSIBILITY_SERVICES)
        fun shell(command: String) =
            ParcelFileDescriptor.AutoCloseInputStream(automation.executeShellCommand(command))
                .bufferedReader()
                .use { it.readText() }
        val prefs = Preferences(context)
        val oldEnd = prefs.focusEnd
        val oldStart = prefs.focusStarted
        val notifications = context.getSystemService(android.app.NotificationManager::class.java)
        try {
            if (android.os.Build.VERSION.SDK_INT >= 33)
                automation.grantRuntimePermission(
                    context.packageName,
                    android.Manifest.permission.POST_NOTIFICATIONS,
                )
            if (android.os.Build.VERSION.SDK_INT >= 31)
                shell("appops set ${context.packageName} SCHEDULE_EXACT_ALARM allow")
            notifications.cancel(80402)
            prefs.focusStarted = System.currentTimeMillis()
            prefs.focusEnd = System.currentTimeMillis() + 4000
            BackgroundTasks.setup(context)
            shell("input keyevent KEYCODE_HOME")
            await("System alarm must finish the session and post a notification") {
                prefs.focusEnd == 0L && notifications.activeNotifications.any { it.id == 80402 }
            }
            assertEquals(
                "Focus session complete",
                notifications.activeNotifications
                    .first { it.id == 80402 }
                    .notification
                    .extras
                    .getString(android.app.Notification.EXTRA_TITLE),
            )
        } finally {
            notifications.cancel(80402)
            prefs.focusEnd = oldEnd
            prefs.focusStarted = oldStart
            BackgroundTasks.scheduleFocus(context)
        }
    }

    @Test
    fun androidScheduledJobArchivesWhileDashboardIsClosed() {
        val automation =
            instrumentation.getUiAutomation(UiAutomation.FLAG_DONT_SUPPRESS_ACCESSIBILITY_SERVICES)
        fun shell(command: String) =
            ParcelFileDescriptor.AutoCloseInputStream(automation.executeShellCommand(command))
                .bufferedReader()
                .use { it.readText() }
        val prefs = Preferences(context)
        val oldBackground = prefs.backgroundArchive
        try {
            shell("appops set ${context.packageName} GET_USAGE_STATS allow")
            prefs.backgroundArchive = true
            prefs.lastArchive = 0
            BackgroundTasks.setup(context)
            shell("input keyevent KEYCODE_HOME")
            shell("cmd jobscheduler run -f ${context.packageName} ${BackgroundTasks.ARCHIVE_JOB}")
            await("Android background job must save a successful archive timestamp") {
                prefs.lastArchive > 0
            }
        } finally {
            prefs.backgroundArchive = oldBackground
            BackgroundTasks.setup(context)
        }
    }

    @Test
    fun realAccessibilityBlockingRechecksRelaunchAndCanBePaused() {
        val automation =
            instrumentation.getUiAutomation(UiAutomation.FLAG_DONT_SUPPRESS_ACCESSIBILITY_SERVICES)
        fun shell(command: String): String =
            ParcelFileDescriptor.AutoCloseInputStream(automation.executeShellCommand(command))
                .bufferedReader()
                .use { it.readText() }
        val oldServices =
            Settings.Secure.getString(
                context.contentResolver,
                Settings.Secure.ENABLED_ACCESSIBILITY_SERVICES,
            )
        val oldEnabled =
            Settings.Secure.getInt(
                context.contentResolver,
                Settings.Secure.ACCESSIBILITY_ENABLED,
                0,
            )
        val prefs = Preferences(context)
        val oldRules = prefs.blockRules
        val oldBlocks = prefs.blocksEnabled
        val oldPause = prefs.blockPauseUntil
        val oldFocusEnd = prefs.focusEnd
        val oldFocusStart = prefs.focusStarted
        val service = "${context.packageName}/${AppBlockService::class.java.name}"
        try {
            assertNotNull(
                "Chrome must be installed on the dedicated emulator",
                context.packageManager.getLaunchIntentForPackage("com.android.chrome"),
            )
            prefs.blockRules = listOf(BlockRule("com.android.chrome", "always"))
            prefs.blocksEnabled = true
            prefs.blockPauseUntil = 0
            shell("settings put secure enabled_accessibility_services $service")
            shell("settings put secure accessibility_enabled 1")
            await("Blocking service must connect") { AppBlockService.connected }
            shell(
                "am start -W -a android.intent.action.MAIN -c android.intent.category.LAUNCHER -p com.android.chrome"
            )
            await("Actual Chrome window must be covered") {
                AppBlockService.blockedApp == "com.android.chrome"
            }
            shell("input keyevent KEYCODE_HOME")
            await("Home must always be usable") { AppBlockService.blockedApp.isEmpty() }
            shell(
                "am start -W -a android.intent.action.MAIN -c android.intent.category.LAUNCHER -p com.android.chrome"
            )
            await("Reopening blocked app must enforce again") {
                AppBlockService.blockedApp == "com.android.chrome"
            }
            prefs.blockPauseUntil = System.currentTimeMillis() + 300_000
            AppBlockService.changed(context)
            await("Pause must remove the overlay") { AppBlockService.blockedApp.isEmpty() }
            prefs.blockPauseUntil = 0
            AppBlockService.changed(context)
            await("Resuming must re-enforce the foreground app") {
                AppBlockService.blockedApp == "com.android.chrome"
            }
            prefs.blocksEnabled = false
            AppBlockService.changed(context)
            await("Turning blocking off must release the app") {
                AppBlockService.blockedApp.isEmpty()
            }
            prefs.blockRules = listOf(BlockRule("com.android.chrome", "focus"))
            prefs.blocksEnabled = true
            prefs.focusStarted = System.currentTimeMillis()
            prefs.focusEnd = System.currentTimeMillis() + 2500
            AppBlockService.changed(context)
            await("Focus rule must cover the active app") {
                AppBlockService.blockedApp == "com.android.chrome"
            }
            await("Focus rule must release at the session deadline without another app switch") {
                AppBlockService.blockedApp.isEmpty()
            }
            assertTrue(AppSafety.protectedPackages(context).contains(context.packageName))
            assertTrue(AppSafety.protectedPackages(context).contains("com.android.settings"))
        } finally {
            prefs.blockRules = oldRules
            prefs.blocksEnabled = oldBlocks
            prefs.blockPauseUntil = oldPause
            prefs.focusEnd = oldFocusEnd
            prefs.focusStarted = oldFocusStart
            BackgroundTasks.scheduleFocus(context)
            AppBlockService.changed(context)
            if (oldServices == null) shell("settings delete secure enabled_accessibility_services")
            else shell("settings put secure enabled_accessibility_services $oldServices")
            shell("settings put secure accessibility_enabled $oldEnabled")
            shell("input keyevent KEYCODE_HOME")
        }
    }

    @Test
    fun completionRecordsOnceAndStopsAlarmState() {
        val prefs = Preferences(context)
        val oldEnd = prefs.focusEnd
        val oldStart = prefs.focusStarted
        try {
            val before = UsageRepository(context, prefs).use { it.completedFocusToday() }
            prefs.focusStarted = System.currentTimeMillis() - 60_000
            prefs.focusEnd = System.currentTimeMillis() - 1000
            BackgroundTasks.finishFocus(context)
            BackgroundTasks.finishFocus(context)
            assertEquals(0L, prefs.focusEnd)
            assertEquals(
                before + 1,
                UsageRepository(context, prefs).use { it.completedFocusToday() },
            )
        } finally {
            prefs.focusEnd = oldEnd
            prefs.focusStarted = oldStart
            BackgroundTasks.scheduleFocus(context)
        }
    }
}
