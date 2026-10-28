package `in`.venusapp.timelens.ui

import android.content.Intent
import android.provider.Settings
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.unit.dp
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import `in`.venusapp.timelens.data.*
import `in`.venusapp.timelens.domain.BlockRule
import `in`.venusapp.timelens.platform.*
import java.util.Locale
import kotlinx.coroutines.delay

@Composable
internal fun FocusScreen(model: AppModel) {
    val state by model.state.collectAsStateWithLifecycle()
    val prefs = model.preferences
    val context = LocalContext.current
    var section by rememberSaveable { mutableIntStateOf(0) }
    var now by remember { mutableLongStateOf(System.currentTimeMillis()) }
    var minutes by rememberSaveable { mutableStateOf(prefs.focusMinutes.toString()) }
    var ruleApp by remember { mutableStateOf<String?>(null) }
    var chooseApp by remember { mutableStateOf(false) }
    var disclosure by remember { mutableStateOf(false) }
    var pending by remember { mutableStateOf<(() -> Unit)?>(null) }
    fun guard(action: () -> Unit) {
        if (prefs.pinHash.isEmpty()) action() else pending = action
    }
    val lifecycle = androidx.lifecycle.compose.LocalLifecycleOwner.current.lifecycle
    val stage by lifecycle.currentStateFlow.collectAsState()
    LaunchedEffect(stage) {
        if (stage.isAtLeast(Lifecycle.State.RESUMED))
            while (true) {
                now = System.currentTimeMillis()
                if (prefs.focusEnd in 1..now) model.refresh()
                delay(1000)
            }
    }
    val remaining = (prefs.focusEnd - now).coerceAtLeast(0)
    val running = remaining > 0
    val paused = prefs.focusPausedMs > 0
    val safe = remember(context) { AppSafety.protectedPackages(context) }
    Column(verticalArrangement = Arrangement.spacedBy(18.dp)) {
        SectionTitle(
            "Protect your attention.",
            "Focus sessions, app limits and quiet routines — together.",
        )
        PrimaryTabRow(selectedTabIndex = section) {
            listOf("Timer", "Blocking", "Activity").forEachIndexed { index, label ->
                Tab(
                    selected = section == index,
                    onClick = { section = index },
                    text = { Text(label) },
                )
            }
        }
        when (section) {
            0 -> {
                Surface(
                    shape = MaterialTheme.shapes.large,
                    color = MaterialTheme.colorScheme.primaryContainer,
                    modifier = Modifier.fillMaxWidth(),
                ) {
                    Column(
                        Modifier.padding(28.dp),
                        horizontalAlignment = Alignment.CenterHorizontally,
                        verticalArrangement = Arrangement.spacedBy(16.dp),
                    ) {
                        Icon(
                            Icons.Outlined.Spa,
                            null,
                            Modifier.size(44.dp),
                            tint = MaterialTheme.colorScheme.primary,
                        )
                        Text(
                            if (running) "IN FOCUS"
                            else if (paused) "PAUSED" else "MAKE SPACE FOR DEEP WORK",
                            style = MaterialTheme.typography.labelMedium,
                            color = MaterialTheme.colorScheme.primary,
                        )
                        val shown =
                            if (running) remaining
                            else if (paused) prefs.focusPausedMs
                            else (minutes.toLongOrNull() ?: 25) * 60_000
                        Text(
                            String.format(
                                Locale.ROOT,
                                "%02d:%02d",
                                shown / 60_000,
                                shown / 1000 % 60,
                            ),
                            style = MaterialTheme.typography.displayLarge,
                            fontWeight = FontWeight.Bold,
                        )
                        Text(
                            "${state.focusCompleted} completed today",
                            style = MaterialTheme.typography.bodySmall,
                        )
                    }
                }
                if (!running && !paused) {
                    Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                        listOf(15, 25, 50, 90).forEach { value ->
                            FilterChip(
                                minutes == value.toString(),
                                onClick = { minutes = value.toString() },
                                label = { Text("${value}m") },
                            )
                        }
                    }
                    OutlinedTextField(
                        minutes,
                        { minutes = it.filter(Char::isDigit).take(3) },
                        label = { Text("Custom duration · 1–240 minutes") },
                        keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                        singleLine = true,
                        modifier = Modifier.fillMaxWidth(),
                    )
                }
                Button(
                    enabled =
                        running || paused || minutes.toIntOrNull()?.let { it in 1..240 } == true,
                    onClick = {
                        if (running)
                            guard {
                                model.edit {
                                    it.preferences.focusPausedMs = remaining
                                    it.preferences.focusEnd = 0
                                }
                            }
                        else
                            model.edit { repo ->
                                val amount =
                                    if (paused) prefs.focusPausedMs
                                    else (minutes.toLongOrNull() ?: 25) * 60_000
                                if (!paused) {
                                    repo.preferences.focusStarted = System.currentTimeMillis()
                                    repo.preferences.focusMinutes = minutes.toInt()
                                }
                                repo.preferences.focusEnd = System.currentTimeMillis() + amount
                                repo.preferences.focusPausedMs = 0
                                if (
                                    repo.preferences.blockRules.any {
                                        it.enabled && it.mode == "focus"
                                    }
                                )
                                    repo.preferences.blocksEnabled = true
                            }
                    },
                    modifier = Modifier.fillMaxWidth(),
                ) {
                    Icon(if (running) Icons.Outlined.Pause else Icons.Outlined.PlayArrow, null)
                    Spacer(Modifier.width(8.dp))
                    Text(
                        if (running) "Pause session"
                        else if (paused) "Resume session" else "Start focus session"
                    )
                }
                if (running || paused)
                    OutlinedButton(
                        onClick = {
                            guard {
                                model.edit { repo ->
                                    if (repo.preferences.focusStarted > 0)
                                        repo.recordFocus(
                                            repo.preferences.focusStarted,
                                            System.currentTimeMillis(),
                                            false,
                                        )
                                    repo.preferences.focusEnd = 0
                                    repo.preferences.focusPausedMs = 0
                                    repo.preferences.focusStarted = 0
                                }
                            }
                        },
                        modifier = Modifier.fillMaxWidth(),
                    ) {
                        Text("End session")
                    }
                InfoCard(
                    if (AppBlockService.connected) "Blocking service connected"
                    else "Enable blocking for a stronger focus session",
                    if (AppBlockService.connected)
                        "${prefs.blockRules.count { it.enabled && it.mode=="focus" }} apps are selected for focus-session blocking. Timer completion can notify you in the background."
                    else
                        "Choose apps under Blocking, read the permission explanation, then enable the optional TimeLens accessibility service.",
                )
                OutlinedButton(onClick = { section = 1 }, modifier = Modifier.fillMaxWidth()) {
                    Text("Choose distracting apps")
                }
            }
            1 -> {
                SettingSwitch(
                    "App blocking",
                    if (AppBlockService.connected) "Accessibility service connected"
                    else "Enable the optional service to enforce your rules",
                    prefs.blocksEnabled,
                    { enabled -> guard { model.edit { it.preferences.blocksEnabled = enabled } } },
                )
                if (!AppBlockService.connected)
                    Button(onClick = { disclosure = true }, modifier = Modifier.fillMaxWidth()) {
                        Text("Set up Android blocking")
                    }
                if (prefs.blockPauseUntil > now)
                    InfoCard(
                        "Blocking paused",
                        "Resumes in ${duration(prefs.blockPauseUntil-now,true)}",
                    )
                Row(horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                    OutlinedButton(
                        onClick = {
                            guard {
                                model.edit {
                                    it.preferences.blockPauseUntil =
                                        if (prefs.blockPauseUntil > now) 0
                                        else System.currentTimeMillis() + 5 * 60_000L
                                }
                            }
                        },
                        modifier = Modifier.weight(1f),
                    ) {
                        Text(if (prefs.blockPauseUntil > now) "Resume" else "Pause 5 min")
                    }
                    Button(
                        onClick = { guard { chooseApp = true } },
                        modifier = Modifier.weight(1f),
                    ) {
                        Icon(Icons.Outlined.Add, null)
                        Text("Add app")
                    }
                }
                if (prefs.blockRules.isEmpty())
                    InfoCard(
                        "Build your block list",
                        "Add installed apps and choose always, during focus, after a daily limit, or scheduled hours.",
                    )
                prefs.blockRules.forEach { rule ->
                    val name =
                        state.installed.firstOrNull { it.app == rule.app }?.label
                            ?: rule.app.substringAfterLast('.')
                    OutlinedCard(
                        onClick = { guard { ruleApp = rule.app } },
                        modifier = Modifier.fillMaxWidth(),
                    ) {
                        Row(
                            Modifier.padding(16.dp),
                            verticalAlignment = Alignment.CenterVertically,
                            horizontalArrangement = Arrangement.spacedBy(12.dp),
                        ) {
                            AppIcon(rule.app, name)
                            Column(Modifier.weight(1f)) {
                                Text(name, fontWeight = FontWeight.SemiBold)
                                Text(
                                    when (rule.mode) {
                                        "always" -> "Always blocked"
                                        "budget" -> "After ${prefs.limit(rule.app)}m daily"
                                        "schedule" -> "Scheduled hours"
                                        else -> "During focus sessions"
                                    },
                                    style = MaterialTheme.typography.bodySmall,
                                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                                )
                            }
                            Switch(
                                rule.enabled,
                                onCheckedChange = { enabled ->
                                    guard {
                                        model.edit {
                                            it.preferences.updateRule(rule.copy(enabled = enabled))
                                        }
                                    }
                                },
                            )
                        }
                    }
                }
                Text(
                    "Android Settings, your launcher, the dialer and TimeLens remain accessible. System settings can disable the service; this is not an unbreakable lock.",
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }
            2 -> {
                Metric(
                    "Prevented openings",
                    state.blocksWeek.toString(),
                    "Last seven days",
                    Modifier.fillMaxWidth(),
                )
                if (state.blocks.isEmpty())
                    InfoCard(
                        "A quieter day starts here",
                        "Blocked openings appear here once the service enforces an app rule.",
                    )
                state.blocks.take(40).forEach { event ->
                    val label =
                        state.installed.firstOrNull { it.app == event.app }?.label
                            ?: event.app.substringAfterLast('.')
                    OutlinedCard(Modifier.fillMaxWidth()) {
                        Row(
                            Modifier.padding(16.dp),
                            horizontalArrangement = Arrangement.spacedBy(12.dp),
                            verticalAlignment = Alignment.CenterVertically,
                        ) {
                            AppIcon(event.app, label)
                            Column(Modifier.weight(1f)) {
                                Text(label, fontWeight = FontWeight.Medium)
                                Text(event.reason, style = MaterialTheme.typography.bodySmall)
                            }
                            Text(timeLabel(event.at), style = MaterialTheme.typography.labelSmall)
                        }
                    }
                }
            }
        }
    }
    if (pending != null)
        PinChallenge(
            prefs,
            onVerified = {
                val action = pending
                pending = null
                action?.invoke()
            },
            onDismiss = { pending = null },
        )
    if (disclosure)
        AlertDialog(
            onDismissRequest = { disclosure = false },
            title = { Text("Enable app blocking") },
            text = {
                Text(
                    "TimeLens uses Android Accessibility to observe app package switches and cover apps you choose. It does not request screen text, website URLs, keyboard input or window trees. Nothing is transmitted. You can pause protection here or disable the service in Android Settings. Sideloaded apps may require Allow restricted settings from their Android App info menu first."
                )
            },
            confirmButton = {
                TextButton(
                    onClick = {
                        disclosure = false
                        runCatching {
                            context.startActivity(Intent(Settings.ACTION_ACCESSIBILITY_SETTINGS))
                        }
                    }
                ) {
                    Text("Open Android Settings")
                }
            },
            dismissButton = { TextButton(onClick = { disclosure = false }) { Text("Not now") } },
        )
    if (chooseApp) {
        var query by remember { mutableStateOf("") }
        AlertDialog(
            onDismissRequest = { chooseApp = false },
            title = { Text("Choose an app") },
            text = {
                Column(
                    Modifier.heightIn(max = 450.dp).verticalScroll(rememberScrollState()),
                    verticalArrangement = Arrangement.spacedBy(8.dp),
                ) {
                    OutlinedTextField(
                        query,
                        { query = it },
                        label = { Text("Search installed apps") },
                        singleLine = true,
                    )
                    state.installed
                        .filter {
                            it.app !in safe &&
                                (it.label.contains(query, true) || it.app.contains(query, true))
                        }
                        .forEach { app ->
                            TextButton(
                                onClick = {
                                    ruleApp = app.app
                                    chooseApp = false
                                },
                                modifier = Modifier.fillMaxWidth(),
                            ) {
                                AppIcon(app.app, app.label, Modifier.size(32.dp))
                                Spacer(Modifier.width(12.dp))
                                Text(app.label, Modifier.weight(1f))
                            }
                        }
                }
            },
            confirmButton = { TextButton(onClick = { chooseApp = false }) { Text("Close") } },
        )
    }
    ruleApp?.let { app ->
        RuleEditor(
            app,
            state.installed.firstOrNull { it.app == app }?.label ?: app,
            prefs,
            model,
            onClose = { ruleApp = null },
        )
    }
}

@Composable
private fun RuleEditor(
    app: String,
    label: String,
    prefs: Preferences,
    model: AppModel,
    onClose: () -> Unit,
) {
    val old = prefs.blockRules.firstOrNull { it.app == app }
    var mode by remember { mutableStateOf(old?.mode ?: "focus") }
    var limit by remember { mutableStateOf(prefs.limit(app).takeIf { it > 0 }?.toString() ?: "30") }
    var start by remember { mutableIntStateOf(old?.startMinute ?: 540) }
    var end by remember { mutableIntStateOf(old?.endMinute ?: 1020) }
    var days by remember { mutableStateOf(old?.days ?: (1..5).toSet()) }
    val context = LocalContext.current
    fun minute(value: Int) = "%02d:%02d".format(value / 60, value % 60)
    AlertDialog(
        onDismissRequest = onClose,
        title = { Text(label) },
        text = {
            Column(
                Modifier.heightIn(max = 480.dp).verticalScroll(rememberScrollState()),
                verticalArrangement = Arrangement.spacedBy(12.dp),
            ) {
                listOf(
                        "focus" to "During focus sessions",
                        "always" to "Always",
                        "budget" to "After a daily limit",
                        "schedule" to "Scheduled hours",
                    )
                    .forEach { (key, title) ->
                        Row(verticalAlignment = Alignment.CenterVertically) {
                            RadioButton(mode == key, onClick = { mode = key })
                            Text(title)
                        }
                    }
                if (mode == "budget")
                    OutlinedTextField(
                        limit,
                        { limit = it.filter(Char::isDigit).take(4) },
                        label = { Text("Daily limit · 1–1440 minutes") },
                        keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                        singleLine = true,
                    )
                if (mode == "schedule") {
                    Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                        OutlinedButton(
                            onClick = {
                                android.app
                                    .TimePickerDialog(
                                        context,
                                        { _, h, m -> start = h * 60 + m },
                                        start / 60,
                                        start % 60,
                                        true,
                                    )
                                    .show()
                            }
                        ) {
                            Text("From ${minute(start)}")
                        }
                        OutlinedButton(
                            onClick = {
                                android.app
                                    .TimePickerDialog(
                                        context,
                                        { _, h, m -> end = h * 60 + m },
                                        end / 60,
                                        end % 60,
                                        true,
                                    )
                                    .show()
                            }
                        ) {
                            Text("To ${minute(end)}")
                        }
                    }
                    Row(
                        Modifier.horizontalScroll(rememberScrollState()),
                        horizontalArrangement = Arrangement.spacedBy(4.dp),
                    ) {
                        listOf("M", "T", "W", "T", "F", "S", "S").forEachIndexed { index, name ->
                            FilterChip(
                                index + 1 in days,
                                onClick = {
                                    days =
                                        if (index + 1 in days) days - (index + 1)
                                        else days + (index + 1)
                                },
                                label = { Text(name) },
                            )
                        }
                    }
                    Text(
                        "Schedules use this phone’s timezone. Overnight schedules continue into the following day.",
                        style = MaterialTheme.typography.bodySmall,
                    )
                }
                if (old != null)
                    TextButton(
                        onClick = {
                            model.edit {
                                it.preferences.blockRules =
                                    prefs.blockRules.filterNot { rule -> rule.app == app }
                            }
                            onClose()
                        }
                    ) {
                        Text("Remove rule", color = MaterialTheme.colorScheme.error)
                    }
            }
        },
        confirmButton = {
            TextButton(
                enabled =
                    (mode != "budget" || limit.toIntOrNull()?.let { it in 1..1440 } == true) &&
                        (mode != "schedule" || days.isNotEmpty()),
                onClick = {
                    model.edit { repo ->
                        if (mode == "budget") repo.preferences.setLimit(app, limit.toInt())
                        repo.preferences.updateRule(BlockRule(app, mode, true, start, end, days))
                        repo.preferences.blocksEnabled = true
                    }
                    onClose()
                },
            ) {
                Text("Save rule")
            }
        },
        dismissButton = { TextButton(onClick = onClose) { Text("Cancel") } },
    )
}
