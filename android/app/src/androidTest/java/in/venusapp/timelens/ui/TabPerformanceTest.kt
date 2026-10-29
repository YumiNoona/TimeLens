package `in`.venusapp.timelens.ui

import android.app.UiAutomation
import android.os.Handler
import android.os.HandlerThread
import android.os.ParcelFileDescriptor
import android.view.FrameMetrics
import android.view.Window
import androidx.compose.ui.test.*
import androidx.compose.ui.test.junit4.createAndroidComposeRule
import androidx.lifecycle.ViewModelProvider
import androidx.test.platform.app.InstrumentationRegistry
import `in`.venusapp.timelens.MainActivity
import `in`.venusapp.timelens.data.ActivityStore
import `in`.venusapp.timelens.domain.Session
import java.time.LocalDate
import java.time.ZoneId
import java.util.concurrent.CopyOnWriteArrayList
import org.junit.Rule
import org.junit.Test

/** A repeatable emulator workload; records measurements, not a physical-device FPS claim. */
class TabPerformanceTest {
    @get:Rule val compose = createAndroidComposeRule<MainActivity>()

    @Test
    fun populatedYearAndRepeatedTabSwitches() {
        val instrumentation = InstrumentationRegistry.getInstrumentation()
        val context = instrumentation.targetContext
        val automation =
            instrumentation.getUiAutomation(UiAutomation.FLAG_DONT_SUPPRESS_ACCESSIBILITY_SERVICES)
        fun shell(command: String) =
            ParcelFileDescriptor.AutoCloseInputStream(automation.executeShellCommand(command))
                .bufferedReader()
                .use { it.readText() }
        val model = ViewModelProvider(compose.activity)[AppModel::class.java]
        val hadAccess =
            `in`.venusapp.timelens.data.UsageRepository(context, model.preferences).use {
                it.hasAccess()
            }
        val frames = CopyOnWriteArrayList<Long>()
        val thread = HandlerThread("TimeLensFrameProbe").apply { start() }
        val listener = Window.OnFrameMetricsAvailableListener { _, metrics, _ ->
            frames.add(metrics.getMetric(FrameMetrics.TOTAL_DURATION))
        }
        try {
            shell("appops set ${context.packageName} GET_USAGE_STATS ignore")
            val midnight =
                LocalDate.now().atStartOfDay(ZoneId.systemDefault()).toInstant().toEpochMilli()
            ActivityStore(context).use { store ->
                store.clear()
                store.restore(
                    (0 until 600).map { i ->
                        Session(
                            "fixture.app.${i%40}",
                            midnight + i * 1000L,
                            midnight + i * 1000L + 900,
                        )
                    } +
                        (1..364).map { d ->
                            Session(
                                "fixture.app.${d%40}",
                                midnight - d * 86_400_000L,
                                midnight - d * 86_400_000L + 900,
                            )
                        }
                )
            }
            compose.runOnUiThread { model.refresh(force = true) }
            compose.waitUntil(30_000) { !model.state.value.loading }
            compose.onNodeWithContentDescription("History", useUnmergedTree = true).performClick()
            compose.onNodeWithText("1 year").performClick()
            compose.waitUntil(30_000) {
                model.state.value.report?.days == 365 && !model.state.value.loading
            }
            compose.waitForIdle()
            compose.runOnUiThread {
                compose.activity.window.addOnFrameMetricsAvailableListener(
                    listener,
                    Handler(thread.looper),
                )
            }
            val switches = mutableListOf<Long>()
            repeat(3) {
                listOf("Apps", "Today", "Block", "Settings", "History").forEach { tab ->
                    val start = System.nanoTime()
                    compose.onNodeWithContentDescription(tab, useUnmergedTree = true).performClick()
                    compose.waitForIdle()
                    switches.add((System.nanoTime() - start) / 1_000_000)
                }
            }
            val sorted = frames.sorted()
            fun percentile(p: Double) =
                if (sorted.isEmpty()) 0 else sorted[((sorted.size - 1) * p).toInt()] / 1_000_000
            android.util.Log.i(
                "TimeLensPerf",
                "tabs_ms=${switches.joinToString(",")} frame_count=${sorted.size} frame_p50_ms=${percentile(.5)} frame_p95_ms=${percentile(.95)} frames_over_32ms=${frames.count {it>32_000_000}}",
            )
        } finally {
            compose.runOnUiThread {
                compose.activity.window.removeOnFrameMetricsAvailableListener(listener)
            }
            thread.quitSafely()
            ActivityStore(context).use { it.clear() }
            shell(
                "appops set ${context.packageName} GET_USAGE_STATS ${if(hadAccess) "allow" else "default"}"
            )
        }
    }
}
