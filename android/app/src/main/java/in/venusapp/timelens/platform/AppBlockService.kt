package `in`.venusapp.timelens.platform

import android.accessibilityservice.AccessibilityService
import android.content.*
import android.graphics.Color
import android.graphics.PixelFormat
import android.os.Handler
import android.os.Looper
import android.view.*
import android.view.accessibility.AccessibilityEvent
import android.widget.*
import androidx.core.content.ContextCompat
import `in`.venusapp.timelens.data.*
import `in`.venusapp.timelens.domain.BlockPolicy
import `in`.venusapp.timelens.domain.PinSecurity
import java.time.LocalDate
import java.time.ZonedDateTime
import kotlinx.coroutines.*

/** Observes package switches only. Never requests a window tree, page text or input events. */
class AppBlockService : AccessibilityService() {
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Main.immediate)
    private val handler = Handler(Looper.getMainLooper())
    private var overlay: View? = null
    private var foreground = ""
    private var overlayApp = ""
    private var task: Job? = null
    private val recheck = Runnable { evaluate(foreground) }
    private val settingsChanged =
        object : BroadcastReceiver() {
            override fun onReceive(context: Context, intent: Intent) {
                evaluate(foreground)
            }
        }

    override fun onServiceConnected() {
        connected = true
        ContextCompat.registerReceiver(
            this,
            settingsChanged,
            IntentFilter(ACTION_CHANGED),
            ContextCompat.RECEIVER_NOT_EXPORTED,
        )
        BackgroundTasks.setup(this)
    }

    override fun onAccessibilityEvent(event: AccessibilityEvent?) {
        if (event?.eventType != AccessibilityEvent.TYPE_WINDOW_STATE_CHANGED) return
        val app = event.packageName?.toString() ?: return
        val keyboard =
            android.provider.Settings.Secure.getString(
                    contentResolver,
                    android.provider.Settings.Secure.DEFAULT_INPUT_METHOD,
                )
                ?.substringBefore('/')
        if (overlay != null && app == keyboard) return
        // Overlay windows are owned by TimeLens, but the covered app is still underneath.
        if (overlay != null && app == packageName) return
        foreground = app
        if (app != overlayApp) removeOverlay()
        evaluate(app)
    }

    private fun safePackages(): Set<String> {
        return AppSafety.protectedPackages(this)
    }

    private fun evaluate(app: String) {
        handler.removeCallbacks(recheck)
        task?.cancel()
        if (app.isEmpty() || app in safePackages()) {
            removeOverlay()
            return
        }
        task = scope.launch {
            try {
                val prefs = Preferences(applicationContext)
                val rule = prefs.blockRules.firstOrNull { it.app == app }
                if (!prefs.blocksEnabled || rule == null) {
                    removeOverlay()
                    return@launch
                }
                val now = System.currentTimeMillis()
                val used =
                    if (rule.mode == "budget")
                        withContext(Dispatchers.IO) {
                            UsageRepository(applicationContext, prefs).use {
                                it.forceArchive()
                                it.load(LocalDate.now())
                                    .apps
                                    .firstOrNull { row -> row.app == app }
                                    ?.milliseconds ?: 0
                            }
                        }
                    else 0L
                if (foreground != app) return@launch
                val decision =
                    BlockPolicy.evaluate(
                        rule,
                        ZonedDateTime.now(),
                        prefs.focusEnd > now,
                        used,
                        prefs.limit(app),
                        prefs.blockPauseUntil > now,
                    )
                if (decision.blocked) {
                    val newOpening = overlay == null || overlayApp != app
                    showOverlay(app, decision.reason, prefs)
                    if (newOpening && overlay != null) {
                        withContext(Dispatchers.IO) {
                            UsageRepository(applicationContext, prefs).use {
                                it.recordBlock(app, decision.reason)
                            }
                        }
                    }
                } else removeOverlay()
                var next = decision.reevaluateInMs.coerceIn(1000, 60_000)
                if (rule.mode == "focus" && prefs.focusEnd > now)
                    next = minOf(next, prefs.focusEnd - now + 50)
                if (rule.mode == "schedule") next = minOf(next, 60_000 - now % 60_000 + 50)
                handler.postDelayed(recheck, next)
            } catch (cancelled: CancellationException) {
                throw cancelled
            } catch (_: Exception) {
                removeOverlay()
            }
        }
    }

    private fun showOverlay(app: String, reason: String, prefs: Preferences) {
        if (overlay != null && overlayApp == app) return
        removeOverlay()
        val label =
            try {
                packageManager
                    .getApplicationLabel(packageManager.getApplicationInfo(app, 0))
                    .toString()
            } catch (_: Exception) {
                app
            }
        val panel =
            LinearLayout(this).apply {
                orientation = LinearLayout.VERTICAL
                gravity = Gravity.CENTER
                setPadding(dp(28), dp(48), dp(28), dp(32))
                setBackgroundColor(Color.rgb(13, 15, 10))
            }
        fun text(value: String, size: Float, color: Int): TextView =
            TextView(this).apply {
                text = value
                textSize = size
                setTextColor(color)
                gravity = Gravity.CENTER
                setPadding(0, dp(10), 0, dp(10))
            }
        panel.addView(text("TIMELENS  /  FOCUS", 13f, Color.rgb(200, 232, 106)))
        runCatching { packageManager.getApplicationIcon(app) }
            .getOrNull()
            ?.let { icon ->
                panel.addView(
                    ImageView(this).apply { setImageDrawable(icon) },
                    LinearLayout.LayoutParams(dp(68), dp(68)).apply {
                        gravity = Gravity.CENTER
                        topMargin = dp(28)
                        bottomMargin = dp(20)
                    },
                )
            }
        panel.addView(text(prefs.blockTitle, 30f, Color.WHITE))
        panel.addView(
            text(prefs.blockMessage.replace("{app}", label), 17f, Color.rgb(185, 194, 175))
        )
        panel.addView(text(reason, 13f, Color.rgb(200, 232, 106)))
        fun button(labelText: String, action: () -> Unit) {
            panel.addView(
                Button(this).apply {
                    text = labelText
                    setOnClickListener { action() }
                },
                LinearLayout.LayoutParams(-1, dp(56)).apply { topMargin = dp(12) },
            )
        }
        button("Back to home") {
            foreground = ""
            performGlobalAction(GLOBAL_ACTION_HOME)
            removeOverlay()
        }
        var pin: EditText? = null
        if (prefs.pinHash.isNotEmpty()) {
            pin =
                EditText(this).apply {
                    hint = "Blocking PIN"
                    setHintTextColor(Color.GRAY)
                    setTextColor(Color.WHITE)
                    inputType =
                        android.text.InputType.TYPE_CLASS_NUMBER or
                            android.text.InputType.TYPE_NUMBER_VARIATION_PASSWORD
                }
            panel.addView(pin, LinearLayout.LayoutParams(-1, dp(56)))
        }
        val status = text("", 13f, Color.rgb(240, 170, 140))
        panel.addView(status)
        button("Pause blocking for 5 minutes") {
            if (System.currentTimeMillis() < prefs.pinRetryAt) {
                status.text = "Too many attempts. Please wait a minute."
                return@button
            }
            val entered = pin?.text?.toString().orEmpty()
            scope.launch {
                val allowed =
                    prefs.pinHash.isEmpty() ||
                        withContext(Dispatchers.Default) {
                            PinSecurity.verify(entered, prefs.pinHash)
                        }
                if (allowed) {
                    prefs.pinFailures = 0
                    prefs.blockPauseUntil = System.currentTimeMillis() + 5 * 60_000L
                    removeOverlay()
                    handler.removeCallbacks(recheck)
                    handler.postDelayed(recheck, 5 * 60_000L)
                } else {
                    prefs.pinFailures += 1
                    if (prefs.pinFailures >= 5) {
                        prefs.pinRetryAt = System.currentTimeMillis() + 60_000
                        prefs.pinFailures = 0
                    }
                    status.text = "Incorrect PIN"
                }
            }
        }
        panel.addView(
            text(
                "You can always manage this service in Android Accessibility settings.",
                12f,
                Color.GRAY,
            )
        )
        val scroll =
            ScrollView(this).apply {
                isFillViewport = true
                addView(panel)
            }
        overlay = scroll
        overlayApp = app
        runCatching {
            getSystemService(WindowManager::class.java)
                .addView(
                    scroll,
                    WindowManager.LayoutParams(
                        -1,
                        -1,
                        WindowManager.LayoutParams.TYPE_ACCESSIBILITY_OVERLAY,
                        WindowManager.LayoutParams.FLAG_LAYOUT_IN_SCREEN,
                        PixelFormat.TRANSLUCENT,
                    ),
                )
        }
            .onSuccess { blockedApp = app }
            .onFailure {
                overlay = null
                overlayApp = ""
            }
    }

    private fun dp(value: Int) = (value * resources.displayMetrics.density).toInt()

    private fun removeOverlay() {
        overlay?.let { runCatching { getSystemService(WindowManager::class.java).removeView(it) } }
        overlay = null
        overlayApp = ""
        blockedApp = ""
    }

    override fun onInterrupt() {
        removeOverlay()
    }

    override fun onDestroy() {
        connected = false
        handler.removeCallbacksAndMessages(null)
        task?.cancel()
        scope.cancel()
        runCatching { unregisterReceiver(settingsChanged) }
        removeOverlay()
        super.onDestroy()
    }

    companion object {
        const val ACTION_CHANGED = "in.venusapp.timelens.BLOCK_SETTINGS_CHANGED"
        @Volatile var connected = false
        @Volatile var blockedApp = ""

        fun changed(context: Context) {
            context.sendBroadcast(Intent(ACTION_CHANGED).setPackage(context.packageName))
        }
    }
}
