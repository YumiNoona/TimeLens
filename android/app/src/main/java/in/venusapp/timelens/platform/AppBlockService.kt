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
import `in`.venusapp.timelens.R
import `in`.venusapp.timelens.data.*
import `in`.venusapp.timelens.domain.BlockDecision
import `in`.venusapp.timelens.domain.BlockPolicy
import `in`.venusapp.timelens.domain.BlockRule
import `in`.venusapp.timelens.domain.PinSecurity
import `in`.venusapp.timelens.domain.WebAddress
import `in`.venusapp.timelens.domain.ThemeMode
import java.time.LocalDate
import java.time.ZonedDateTime
import kotlinx.coroutines.*

/** App switches plus opt-in, fixed-ID browser address bars. Never traverses page content. */
class AppBlockService : AccessibilityService() {
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Main.immediate)
    private val handler = Handler(Looper.getMainLooper())
    private var overlay: View? = null
    private var foreground = ""
    private var overlayApp = ""
    private var overlayTarget = ""
    private var coveredAt = 0L
    private var host = ""
    private var observedAt = 0L
    private var browserTask: Job? = null
    private val browserPoll =
        object : Runnable {
            override fun run() {
                if (
                    interactive() && BrowserAddressReader.supported(foreground) &&
                        Preferences(applicationContext).websiteTracking
                ) {
                    pollBrowser()
                    handler.postDelayed(this, 1000)
                }
            }
        }
    private var task: Job? = null
    private val recheck = Runnable { evaluate(foreground) }
    private fun interactive() = getSystemService(android.os.PowerManager::class.java).isInteractive &&
        !getSystemService(android.app.KeyguardManager::class.java).isKeyguardLocked
    private val screenChanges = object : BroadcastReceiver() {
        override fun onReceive(context: Context, intent: Intent) {
            handler.removeCallbacks(browserPoll)
            handler.removeCallbacks(recheck)
            browserTask?.cancel()
            task?.cancel()
            host = ""
            websiteHost = ""
            observedAt = 0
            removeOverlay()
            if (intent.action == Intent.ACTION_USER_PRESENT && interactive()) {
                pollBrowser()
                if (Preferences(applicationContext).websiteTracking && BrowserAddressReader.supported(foreground))
                    handler.postDelayed(browserPoll, 1000)
                evaluate(foreground)
            }
        }
    }
    private val settingsChanged =
        object : BroadcastReceiver() {
            override fun onReceive(context: Context, intent: Intent) {
                task?.cancel()
                configureBrowserAccess()
                removeOverlay()
                handler.removeCallbacks(browserPoll)
                pollBrowser()
                if (
                    interactive() && Preferences(applicationContext).websiteTracking &&
                        BrowserAddressReader.supported(foreground)
                )
                    handler.postDelayed(browserPoll, 1000)
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
        configureBrowserAccess()
        ContextCompat.registerReceiver(this, screenChanges,
            IntentFilter(Intent.ACTION_SCREEN_OFF).apply { addAction(Intent.ACTION_USER_PRESENT) },
            ContextCompat.RECEIVER_NOT_EXPORTED)
        BackgroundTasks.setup(this)
    }

    private fun configureBrowserAccess() {
        val enabled = Preferences(applicationContext).websiteTracking
        serviceInfo = serviceInfo.apply {
            // Content-change events were discarded by onAccessibilityEvent anyway. Browser
            // address polling already handles SPA navigation without subscribing to page churn.
            eventTypes = AccessibilityEvent.TYPE_WINDOW_STATE_CHANGED
            flags =
                if (enabled)
                    flags or
                        android.accessibilityservice.AccessibilityServiceInfo.FLAG_REPORT_VIEW_IDS
                else
                    flags and
                        android.accessibilityservice.AccessibilityServiceInfo.FLAG_REPORT_VIEW_IDS
                            .inv()
        }
        if (!enabled) {
            host = ""
            websiteHost = ""
            observedAt = 0
            handler.removeCallbacks(browserPoll)
        }
    }

    override fun onAccessibilityEvent(event: AccessibilityEvent?) {
        if (event == null || !interactive()) return
        val app = event.packageName?.toString() ?: return
        if (event.eventType == AccessibilityEvent.TYPE_WINDOW_CONTENT_CHANGED) return
        if (event.eventType != AccessibilityEvent.TYPE_WINDOW_STATE_CHANGED) return
        val keyboard =
            android.provider.Settings.Secure.getString(
                    contentResolver,
                    android.provider.Settings.Secure.DEFAULT_INPUT_METHOD,
                )
                ?.substringBefore('/')
        if (app == keyboard) {
            host = ""
            websiteHost = ""
            observedAt = 0
            return
        }
        if (overlay != null && app == packageName) return
        if (foreground != app) {
            handler.removeCallbacks(browserPoll)
            browserTask?.cancel()
            host = ""
            websiteHost = ""
            observedAt = 0
        }
        foreground = app
        if (app != overlayApp) removeOverlay()
        if (
            Preferences(applicationContext).websiteTracking && BrowserAddressReader.supported(app)
        ) {
            handler.removeCallbacks(browserPoll)
            pollBrowser()
            handler.postDelayed(browserPoll, 1000)
        }
        evaluate(app)
    }

    private fun pollBrowser() {
        if (browserTask?.isActive == true) return
        val prefs = Preferences(applicationContext)
        if (!prefs.websiteTracking || !BrowserAddressReader.supported(foreground)) return
        val now = System.currentTimeMillis()
        val interactive =
            getSystemService(android.os.PowerManager::class.java).isInteractive &&
                !getSystemService(android.app.KeyguardManager::class.java).isKeyguardLocked
        if (!interactive) {
            host = ""
            websiteHost = ""
            observedAt = 0
            removeOverlay()
            return
        }
        if (overlay != null) {
            observedAt = now
            return
        }
        val next = runCatching {
            val root = rootInActiveWindow
            if (root != null) BrowserAddressReader.host(root, foreground).orEmpty() else ""
        }
            .getOrDefault("")
        val previous = host
        val from = observedAt
        host = next
        websiteHost = next
        observedAt = now
        browserTask = scope.launch {
            try {
                if (
                    next.isNotEmpty() &&
                        previous == next &&
                        from > 0 &&
                        now - from in 1..5000 &&
                        prefs.pauseTrackingUntil <= now
                ) {
                    withContext(Dispatchers.IO) {
                        SiteStore(applicationContext).use { it.observe(next, from, now) }
                    }
                }
                evaluate(foreground)
            } catch (cancelled: CancellationException) {
                throw cancelled
            } catch (_: Exception) {
                host = ""
                websiteHost = ""
                observedAt = 0
            }
        }
    }

    private fun safePackages(): Set<String> {
        return AppSafety.protectedPackages(this)
    }

    private fun evaluate(app: String) {
        handler.removeCallbacks(recheck)
        task?.cancel()
        if (!interactive() || app.isEmpty() || app in safePackages()) {
            removeOverlay()
            return
        }
        task = scope.launch {
            var guarded: Pair<String, Preferences>? = null
            try {
                val prefs = Preferences(applicationContext)
                val now = System.currentTimeMillis()
                checkpointCoverage(now)
                if (!prefs.blocksEnabled) {
                    removeOverlay()
                    return@launch
                }
                val appRule = prefs.blockRules.firstOrNull { it.app == app && it.enabled }
                val siteRules =
                    if (prefs.websiteTracking && host.isNotEmpty())
                        WebAddress.matchingRules(prefs.blockRules, host)
                    else emptyList()
                val candidate = appRule?.takeIf { it.mode in listOf("strict", "comfort") }
                    ?: siteRules.firstOrNull { it.mode in listOf("strict", "comfort") }
                guarded = candidate?.let { it.app to prefs }
                val websiteUsage = if (siteRules.any { it.mode == "comfort" })
                    withContext(Dispatchers.IO) {
                        SiteStore(applicationContext).use { it.read(now - 86_400_000L, now) }
                    } else emptyList()
                suspend fun decide(rule: BlockRule?): BlockDecision {
                    if (rule == null) return BlockDecision(false)
                    val used: Long? =
                        if (rule.mode == "comfort")
                            withContext(Dispatchers.IO) {
                                if (rule.app.startsWith("web:"))
                                    websiteUsage
                                            .filter {
                                                WebAddress.matches(
                                                    it.app,
                                                    rule.app.removePrefix("web:"),
                                                )
                                            }
                                            .sumOf { it.milliseconds }
                                else
                                    UsageRepository(applicationContext, prefs).use {
                                        it.allowanceUsage(app, now)
                                    }
                            }
                        else if (rule.mode == "budget")
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
                    if (used == null)
                        return BlockDecision(
                            true,
                            "Usage access is needed to measure the Comfort allowance",
                        )
                    return BlockPolicy.evaluate(
                        rule,
                        java.time.ZonedDateTime.now(),
                        prefs.focusEnd > now,
                        used,
                        prefs.limit(app),
                        prefs.blockPauseUntil > now,
                    )
                }
                val appDecision = decide(appRule)
                val siteDecisions = siteRules.map { it to decide(it) }
                if (foreground != app) return@launch
                val blockedSite = siteDecisions.firstOrNull { it.second.blocked }
                val rule = if (appDecision.blocked) appRule else blockedSite?.first
                val decision = if (appDecision.blocked) appDecision else blockedSite?.second ?: BlockDecision(false)
                if (decision.blocked && rule != null) {
                    val newOpening = overlay == null || overlayTarget != rule.app
                    showOverlay(app, decision.reason, prefs, rule.app)
                    if (newOpening && overlay != null)
                        withContext(Dispatchers.IO) {
                            UsageRepository(applicationContext, prefs).use {
                                it.recordBlock(rule.app, decision.reason)
                            }
                        }
                } else removeOverlay()
                var next =
                    minOf(appDecision.reevaluateInMs, siteDecisions.minOfOrNull { it.second.reevaluateInMs } ?: 60_000)
                        .coerceIn(250, 60_000)
                if (appRule?.mode == "focus" && prefs.focusEnd > now)
                    next = minOf(next, prefs.focusEnd - now + 50)
                if (appRule?.mode == "schedule") next = minOf(next, 60_000 - now % 60_000 + 50)
                if (prefs.blockPauseUntil > now)
                    next = minOf(next, prefs.blockPauseUntil - now + 50)

                handler.postDelayed(recheck, next)
            } catch (cancelled: CancellationException) {
                throw cancelled
            } catch (_: Exception) {
                // A transient measurement/database error must not silently open a protected app.
                val fallback = guarded
                if (fallback != null && foreground == app && interactive()) {
                    runCatching { showOverlay(app, "Unable to verify allowance · retrying", fallback.second, fallback.first) }
                    handler.postDelayed(recheck, 2000)
                } else removeOverlay()
            }
        }
    }

    private fun showOverlay(app: String, reason: String, prefs: Preferences, target: String = app) {
        if (overlay != null && overlayApp == app && overlayTarget == target) return
        removeOverlay()
        val dark = ThemeMode.isDark(prefs.theme,
            resources.configuration.uiMode and android.content.res.Configuration.UI_MODE_NIGHT_MASK ==
                android.content.res.Configuration.UI_MODE_NIGHT_YES)
        val label =
            if (target.startsWith("web:")) target.removePrefix("web:")
            else
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
                setBackgroundColor(
                    if (!dark) Color.WHITE else Color.rgb(11, 11, 11)
                )
            }
        fun text(value: String, size: Float, color: Int): TextView =
            TextView(this).apply {
                text = value
                typeface =
                    androidx.core.content.res.ResourcesCompat.getFont(
                        this@AppBlockService,
                        R.font.space_grotesk,
                    )
                textSize = size
                setTextColor(color)
                gravity = Gravity.CENTER
                setPadding(0, dp(10), 0, dp(10))
            }
        val ink = if (!dark) Color.BLACK else Color.WHITE
        val muted = if (!dark) Color.DKGRAY else Color.LTGRAY
        panel.addView(text("TIMELENS / BLOCK", 13f, muted))
        if (target.startsWith("web:")) panel.addView(text("◎", 52f, ink))
        else
            runCatching { packageManager.getApplicationIcon(app) }
                .getOrNull()
                ?.let { icon ->
                    panel.addView(
                        ImageView(this).apply {
                            setImageDrawable(icon)
                            if (prefs.grayIcons)
                                colorFilter =
                                    android.graphics.ColorMatrixColorFilter(
                                        android.graphics.ColorMatrix().apply { setSaturation(0f) }
                                    )
                        },
                        LinearLayout.LayoutParams(dp(68), dp(68)).apply {
                            gravity = Gravity.CENTER
                            topMargin = dp(28)
                            bottomMargin = dp(20)
                        },
                    )
                }
        panel.addView(text(prefs.blockTitle, 30f, ink))
        panel.addView(text(prefs.blockMessage.replace("{app}", label), 17f, muted))
        panel.addView(text(reason, 13f, muted))
        fun button(labelText: String, action: () -> Unit) {
            panel.addView(
                Button(this).apply {
                    text = labelText
                    typeface =
                        androidx.core.content.res.ResourcesCompat.getFont(
                            this@AppBlockService,
                            R.font.space_grotesk,
                        )
                    setTextColor(if (!dark) Color.WHITE else Color.BLACK)
                    backgroundTintList = android.content.res.ColorStateList.valueOf(ink)
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
        if (target.startsWith("web:"))
            button("Go back in browser") {
                performGlobalAction(GLOBAL_ACTION_BACK)
                removeOverlay()
                host = ""
                websiteHost = ""
                observedAt = 0
            }
        val legacyPause =
            prefs.blockRules.firstOrNull { it.app == target }?.mode !in listOf("strict", "comfort")
        if (legacyPause) {
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
        overlayTarget = target
        coveredAt = System.currentTimeMillis()
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
            .onSuccess {
                blockedApp = app
                blockedTarget = target
            }
            .onFailure {
                overlay = null
                overlayApp = ""
            }
    }

    private fun dp(value: Int) = (value * resources.displayMetrics.density).toInt()

    private fun checkpointCoverage(now: Long) {
        if (overlay != null && overlayApp.isNotEmpty() && coveredAt > 0 && now > coveredAt) {
            runCatching {
                SiteStore(applicationContext).use { it.cover(overlayApp, coveredAt, now) }
            }
            coveredAt = now
        }
    }

    private fun removeOverlay() {
        checkpointCoverage(System.currentTimeMillis())
        overlay?.let { runCatching { getSystemService(WindowManager::class.java).removeView(it) } }
        overlay = null
        overlayApp = ""
        overlayTarget = ""
        coveredAt = 0
        blockedApp = ""
        blockedTarget = ""
    }

    override fun onInterrupt() {
        removeOverlay()
    }

    override fun onDestroy() {
        connected = false
        handler.removeCallbacksAndMessages(null)
        task?.cancel()
        browserTask?.cancel()
        scope.cancel()
        runCatching { unregisterReceiver(settingsChanged) }
        runCatching { unregisterReceiver(screenChanges) }
        removeOverlay()
        super.onDestroy()
    }

    companion object {
        const val ACTION_CHANGED = "in.venusapp.timelens.BLOCK_SETTINGS_CHANGED"
        @Volatile var connected = false
        @Volatile var blockedApp = ""
        @Volatile var websiteHost = ""
        @Volatile var blockedTarget = ""

        fun changed(context: Context) {
            context.sendBroadcast(Intent(ACTION_CHANGED).setPackage(context.packageName))
        }
    }
}
