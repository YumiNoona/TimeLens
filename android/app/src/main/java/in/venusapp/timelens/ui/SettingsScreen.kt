package `in`.venusapp.timelens.ui

import android.Manifest
import android.app.AlarmManager
import android.app.NotificationManager
import android.content.Intent
import android.content.pm.PackageManager
import android.net.Uri
import android.os.Build
import android.provider.Settings
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.*
import androidx.compose.ui.unit.dp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import `in`.venusapp.timelens.BuildConfig
import `in`.venusapp.timelens.data.*
import `in`.venusapp.timelens.domain.PinSecurity
import `in`.venusapp.timelens.platform.*
import kotlinx.coroutines.*

@Composable
internal fun SettingsScreen(
    model: AppModel,
    onAccess: () -> Unit,
    onExport: (Boolean, Int) -> Unit,
    onImport: () -> Unit,
    exportEnabled: Boolean,
) {
    val state by model.state.collectAsStateWithLifecycle()
    val prefs = model.preferences
    val context = LocalContext.current
    var budget by remember { mutableStateOf(prefs.budget.toString()) }
    var delete by remember { mutableStateOf(false) }
    var range by remember { mutableIntStateOf(1) }
    var title by remember { mutableStateOf(prefs.blockTitle) }
    var message by remember { mutableStateOf(prefs.blockMessage) }
    var pinSetup by remember { mutableStateOf(false) }
    var pending by remember { mutableStateOf<(() -> Unit)?>(null) }
    val notifications =
        rememberLauncherForActivityResult(ActivityResultContracts.RequestPermission()) {
            model.refresh()
        }
    fun guard(action: () -> Unit) {
        if (prefs.pinHash.isEmpty()) action() else pending = action
    }
    fun open(action: String, data: Uri? = null) {
        runCatching { context.startActivity(Intent(action, data)) }
    }
    @Composable
    fun Group(titleText: String, content: @Composable ColumnScope.() -> Unit) {
        OutlinedCard(Modifier.fillMaxWidth()) {
            Column(Modifier.padding(20.dp), verticalArrangement = Arrangement.spacedBy(14.dp)) {
                Text(
                    titleText,
                    style = MaterialTheme.typography.titleMedium,
                    fontWeight = FontWeight.SemiBold,
                )
                content()
            }
        }
    }
    Column(verticalArrangement = Arrangement.spacedBy(18.dp)) {
        SectionTitle(
            "Make it feel like yours.",
            "Appearance, tracking, privacy and protection in one place.",
        )
        Group("Permissions & device health") {
            PermissionRow(
                "Usage access",
                state.day?.hasAccess == true,
                "Read local foreground activity",
                onAccess,
            )
            PermissionRow(
                "App blocking",
                AppBlockService.connected,
                "Optional Accessibility service",
            ) {
                open(Settings.ACTION_ACCESSIBILITY_SETTINGS)
            }
            val granted =
                context.getSystemService(NotificationManager::class.java).areNotificationsEnabled()
            PermissionRow("Notifications", granted, "Background focus completion alerts") {
                if (
                    Build.VERSION.SDK_INT >= 33 &&
                        context.checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) !=
                            PackageManager.PERMISSION_GRANTED
                )
                    notifications.launch(Manifest.permission.POST_NOTIFICATIONS)
                else
                    runCatching {
                        context.startActivity(
                            Intent(Settings.ACTION_APP_NOTIFICATION_SETTINGS)
                                .putExtra(Settings.EXTRA_APP_PACKAGE, context.packageName)
                        )
                    }
            }
            if (Build.VERSION.SDK_INT >= 31)
                PermissionRow(
                    "Precise timer alerts",
                    context.getSystemService(AlarmManager::class.java).canScheduleExactAlarms(),
                    "Optional; Android may delay alerts otherwise",
                ) {
                    open(
                        Settings.ACTION_REQUEST_SCHEDULE_EXACT_ALARM,
                        Uri.parse("package:${context.packageName}"),
                    )
                }
        }
        Group("Appearance") {
            Row(
                Modifier.horizontalScroll(rememberScrollState()),
                horizontalArrangement = Arrangement.spacedBy(8.dp),
            ) {
                listOf("dark", "light", "system").forEach { value ->
                    FilterChip(
                        prefs.theme == value,
                        onClick = { model.edit { it.preferences.theme = value } },
                        label = { Text(value.replaceFirstChar(Char::uppercase)) },
                    )
                }
            }
            Text("Accent colour", style = MaterialTheme.typography.labelMedium)
            Row(
                Modifier.horizontalScroll(rememberScrollState()),
                horizontalArrangement = Arrangement.spacedBy(14.dp),
            ) {
                AccentColors.forEach { (key, color) ->
                    Column(
                        horizontalAlignment = androidx.compose.ui.Alignment.CenterHorizontally,
                        verticalArrangement = Arrangement.spacedBy(6.dp),
                    ) {
                        Box(
                            Modifier.size(40.dp).clip(CircleShape).background(color).clickable {
                                model.edit { it.preferences.accent = key }
                            },
                            contentAlignment = androidx.compose.ui.Alignment.Center,
                        ) {
                            if (prefs.accent == key)
                                Icon(
                                    Icons.Outlined.Check,
                                    null,
                                    tint = androidx.compose.ui.graphics.Color.Black,
                                )
                        }
                        Text(
                            key.replaceFirstChar(Char::uppercase),
                            style = MaterialTheme.typography.labelSmall,
                        )
                    }
                }
            }
            SettingSwitch(
                "Compact cards",
                "Fit more on the screen",
                prefs.compact,
                { value -> model.edit { it.preferences.compact = value } },
            )
            SettingSwitch(
                "24-hour clock",
                "Use 00:00–23:59 in timelines",
                prefs.use24Hour,
                { value -> model.edit { it.preferences.use24Hour = value } },
            )
            SettingSwitch(
                "Show seconds",
                "Detailed durations in cards and app views",
                prefs.showSeconds,
                { value -> model.edit { it.preferences.showSeconds = value } },
            )
        }
        Group("Tracking & storage") {
            SettingSwitch(
                "Background archival",
                "Android schedules a local import about every 15 minutes; battery controls can defer it",
                prefs.backgroundArchive,
                { value -> model.edit { it.preferences.backgroundArchive = value } },
            )
            Text(
                if (prefs.lastArchive > 0) "Last saved: ${timeLabel(prefs.lastArchive)}"
                else "No successful archive yet",
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
            OutlinedButton(onClick = { model.archiveNow() }, modifier = Modifier.fillMaxWidth()) {
                Icon(Icons.Outlined.Sync, null)
                Spacer(Modifier.width(10.dp))
                Text("Archive recent activity now")
            }
            Row(
                Modifier.horizontalScroll(rememberScrollState()),
                horizontalArrangement = Arrangement.spacedBy(8.dp),
            ) {
                listOf(7, 30, 90, 365).forEach { value ->
                    FilterChip(
                        prefs.retention == value,
                        onClick = { model.edit { it.preferences.retention = value } },
                        label = { Text("${value} days") },
                    )
                }
            }
            Text(
                "Retention limits TimeLens’s local archive. Android may discard source events earlier.",
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
        }
        Group("Daily goals & categories") {
            OutlinedTextField(
                budget,
                { budget = it.filter(Char::isDigit).take(4) },
                label = { Text("Daily screen budget · 15–1440 minutes") },
                keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                singleLine = true,
                modifier = Modifier.fillMaxWidth(),
            )
            Button(
                enabled = budget.toIntOrNull()?.let { it in 15..1440 } == true,
                onClick = { model.edit { it.preferences.budget = budget.toInt() } },
            ) {
                Text("Save daily budget")
            }
            Text("Productive categories", fontWeight = FontWeight.Medium)
            Text(
                "Your choices define the focus score. There is no universal productive app list.",
                style = MaterialTheme.typography.bodySmall,
            )
            Preferences.categories.chunked(2).forEach { row ->
                Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    row.forEach { name ->
                        FilterChip(
                            name in prefs.productiveCategories,
                            onClick = {
                                model.edit {
                                    it.preferences.productiveCategories =
                                        if (name in prefs.productiveCategories)
                                            prefs.productiveCategories - name
                                        else prefs.productiveCategories + name
                                }
                            },
                            label = { Text(name) },
                            modifier = Modifier.weight(1f),
                        )
                    }
                }
            }
        }
        Group("Privacy & exclusions") {
            val paused = prefs.pauseTrackingUntil > System.currentTimeMillis()
            Text(
                if (paused) "Tracking is paused" else "Tracking is active",
                fontWeight = FontWeight.Medium,
            )
            OutlinedButton(
                onClick = {
                    model.edit {
                        if (paused) it.preferences.resumeTracking()
                        else it.preferences.pauseTracking(30)
                        it.forceArchive()
                    }
                },
                modifier = Modifier.fillMaxWidth(),
            ) {
                Text(if (paused) "Resume tracking" else "Pause tracking for 30 minutes")
            }
            Text(
                "Paused intervals are removed from later imports too. Exclude an app from its details card to erase its saved activity.",
                style = MaterialTheme.typography.bodySmall,
            )
            prefs.excluded.sorted().forEach { app ->
                TextButton(onClick = { model.edit { it.preferences.restore(app) } }) {
                    Text("Restore: ${state.installed.firstOrNull {it.app==app}?.label ?: app}")
                }
            }
        }
        Group("Blocking appearance & PIN") {
            OutlinedTextField(
                title,
                { title = it.take(100) },
                label = { Text("Blocking screen title") },
                modifier = Modifier.fillMaxWidth(),
            )
            OutlinedTextField(
                message,
                { message = it.take(500) },
                label = { Text("Message · use {app} for the app name") },
                modifier = Modifier.fillMaxWidth(),
                minLines = 2,
            )
            Button(
                onClick = {
                    model.edit {
                        it.preferences.blockTitle = title
                        it.preferences.blockMessage = message
                    }
                }
            ) {
                Text("Save blocking screen")
            }
            OutlinedButton(
                onClick = { guard { pinSetup = true } },
                modifier = Modifier.fillMaxWidth(),
            ) {
                Icon(Icons.Outlined.Lock, null)
                Spacer(Modifier.width(10.dp))
                Text(if (prefs.pinHash.isEmpty()) "Set a blocking PIN" else "Change blocking PIN")
            }
            if (prefs.pinHash.isNotEmpty())
                TextButton(onClick = { guard { model.edit { it.preferences.pinHash = "" } } }) {
                    Text("Remove PIN")
                }
            Text(
                "The PIN protects TimeLens blocking controls and pause actions. Android system settings remain available for recovery.",
                style = MaterialTheme.typography.bodySmall,
            )
        }
        Group("Export your activity") {
            Text(
                "Ends on ${state.date}. Files contain private activity; choose where to save them.",
                style = MaterialTheme.typography.bodySmall,
            )
            Row(
                Modifier.horizontalScroll(rememberScrollState()),
                horizontalArrangement = Arrangement.spacedBy(8.dp),
            ) {
                listOf(1, 7, 30, 90).forEach { value ->
                    FilterChip(
                        range == value,
                        onClick = { range = value },
                        label = { Text(if (value == 1) "Selected day" else "${value} days") },
                    )
                }
            }
            Row(horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                OutlinedButton(
                    enabled = exportEnabled,
                    onClick = { onExport(true, range) },
                    modifier = Modifier.weight(1f),
                ) {
                    Text("Save JSON")
                }
                OutlinedButton(
                    enabled = exportEnabled,
                    onClick = { onExport(false, range) },
                    modifier = Modifier.weight(1f),
                ) {
                    Text("Save CSV")
                }
            }
        }
        OutlinedButton(onClick = onImport, modifier = Modifier.fillMaxWidth()) {
            Icon(Icons.Outlined.Restore, null)
            Spacer(Modifier.width(10.dp))
            Text("Restore Android JSON export")
        }
        InfoCard(
            "Independent. Private. Local.",
            "Android ${BuildConfig.VERSION_NAME} · No account, analytics or Internet permission. This app tracks this phone independently. Local PC pairing and browser/media detail still require separate integrations.",
        )
        OutlinedButton(onClick = { guard { delete = true } }, modifier = Modifier.fillMaxWidth()) {
            Text("Delete all saved phone activity", color = MaterialTheme.colorScheme.error)
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
    if (pinSetup) {
        var pin by remember { mutableStateOf("") }
        var confirmation by remember { mutableStateOf("") }
        var busy by remember { mutableStateOf(false) }
        val scope = rememberCoroutineScope()
        AlertDialog(
            onDismissRequest = { if (!busy) pinSetup = false },
            title = { Text("Set blocking PIN") },
            text = {
                Column(verticalArrangement = Arrangement.spacedBy(12.dp)) {
                    OutlinedTextField(
                        pin,
                        { pin = it.filter(Char::isDigit).take(12) },
                        label = { Text("4–12 digit PIN") },
                        visualTransformation = PasswordVisualTransformation(),
                        keyboardOptions =
                            KeyboardOptions(keyboardType = KeyboardType.NumberPassword),
                        singleLine = true,
                    )
                    OutlinedTextField(
                        confirmation,
                        { confirmation = it.filter(Char::isDigit).take(12) },
                        label = { Text("Confirm PIN") },
                        visualTransformation = PasswordVisualTransformation(),
                        keyboardOptions =
                            KeyboardOptions(keyboardType = KeyboardType.NumberPassword),
                        singleLine = true,
                    )
                    Text(
                        "Keep your PIN safe. You can always disable the service from Android settings.",
                        style = MaterialTheme.typography.bodySmall,
                    )
                }
            },
            confirmButton = {
                TextButton(
                    enabled = !busy && pin.length >= 4 && pin == confirmation,
                    onClick = {
                        busy = true
                        scope.launch {
                            val hash = withContext(Dispatchers.Default) { PinSecurity.hash(pin) }
                            model.edit { it.preferences.pinHash = hash }
                            pinSetup = false
                        }
                    },
                ) {
                    Text(if (busy) "Saving…" else "Save PIN")
                }
            },
            dismissButton = {
                TextButton(enabled = !busy, onClick = { pinSetup = false }) { Text("Cancel") }
            },
        )
    }
    if (delete)
        AlertDialog(
            onDismissRequest = { delete = false },
            title = { Text("Delete saved phone activity?") },
            text = {
                Text(
                    "This deletes saved activity, focus history and block logs, and moves the import boundary to now. Deleted history will not return on refresh. Rules, app preferences, Android system history and previously exported files remain."
                )
            },
            confirmButton = {
                TextButton(
                    onClick = {
                        model.edit { it.clear() }
                        delete = false
                    }
                ) {
                    Text("Delete activity")
                }
            },
            dismissButton = { TextButton(onClick = { delete = false }) { Text("Cancel") } },
        )
}

@Composable
private fun PermissionRow(
    title: String,
    enabled: Boolean,
    description: String,
    onOpen: () -> Unit,
) {
    Row(
        verticalAlignment = androidx.compose.ui.Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(12.dp),
    ) {
        Icon(
            if (enabled) Icons.Outlined.CheckCircle else Icons.Outlined.Info,
            null,
            tint =
                if (enabled) MaterialTheme.colorScheme.primary
                else MaterialTheme.colorScheme.onSurfaceVariant,
        )
        Column(Modifier.weight(1f)) {
            Text(title, fontWeight = FontWeight.Medium)
            Text(
                description,
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
        }
        TextButton(onClick = onOpen) { Text(if (enabled) "Manage" else "Enable") }
    }
}
