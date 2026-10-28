package `in`.venusapp.timelens.ui

import android.content.Intent
import android.provider.Settings
import androidx.compose.foundation.clickable
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
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import `in`.venusapp.timelens.domain.BlockRule
import `in`.venusapp.timelens.domain.WebAddress
import `in`.venusapp.timelens.platform.AppBlockService
import `in`.venusapp.timelens.platform.AppSafety

@Composable
internal fun BlockScreen(model: AppModel) {
    val state by model.state.collectAsStateWithLifecycle()
    val prefs = model.preferences
    val context = LocalContext.current
    var section by rememberSaveable { mutableIntStateOf(0) }
    var chooser by rememberSaveable { mutableStateOf(false) }
    var editor by remember { mutableStateOf<BlockRule?>(null) }
    var pending by remember { mutableStateOf<(() -> Unit)?>(null) }
    var disclosure by rememberSaveable { mutableStateOf(false) }
    var websites by rememberSaveable { mutableStateOf(false) }
    var banner by rememberSaveable { mutableStateOf(false) }
    val safe = remember(context) { AppSafety.protectedPackages(context) }
    fun guard(action: () -> Unit) {
        if (prefs.pinHash.isEmpty()) action() else pending = action
    }
    fun openAccess() {
        runCatching { context.startActivity(Intent(Settings.ACTION_ACCESSIBILITY_SETTINGS)) }
    }
    Column(verticalArrangement = Arrangement.spacedBy(18.dp)) {
        SectionTitle("Make room for life.", "Choose an allowance or keep distractions closed.")
        OutlinedCard(Modifier.fillMaxWidth()) {
            Column(Modifier.padding(20.dp)) {
                SettingSwitch(
                    "Blocking",
                    if (AppBlockService.connected) "Accessibility service connected"
                    else "Enable the Android service to enforce rules",
                    prefs.blocksEnabled,
                ) { value ->
                    guard {
                        if (value && !AppBlockService.connected) disclosure = true
                        else model.edit { it.preferences.blocksEnabled = value }
                    }
                }
                if (!AppBlockService.connected)
                    TextButton(onClick = { disclosure = true }) {
                        Icon(Icons.Outlined.VerifiedUser, null)
                        Spacer(Modifier.width(8.dp))
                        Text("Set up blocking")
                    }
                Text(
                    "Comfort · a time allowance in the last 24 hours\nStrict · no access while the rule is enabled",
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }
        }
        PrimaryTabRow(selectedTabIndex = section) {
            listOf(
                    Triple(Icons.Outlined.Apps, "App rules", 0),
                    Triple(Icons.Outlined.Language, "Website rules", 1),
                    Triple(Icons.Outlined.History, "Block activity", 2),
                )
                .forEach { (icon, label, index) ->
                    Tab(
                        section == index,
                        onClick = { section = index },
                        icon = { Icon(icon, label) },
                    )
                }
        }
        if (section < 2) {
            if (section == 1) {
                SettingSwitch(
                    "Website tracking",
                    "Optional · read visible browser address bars and save domains on this phone",
                    prefs.websiteTracking,
                ) { enabled ->
                    if (enabled) websites = true
                    else guard { model.edit { it.preferences.websiteTracking = false } }
                }
                InfoCard(
                    "Supported browser integration",
                    "Chrome, Firefox, Edge and Samsung Internet address bars are recognized when exposed by the browser. Hidden bars, private browsing and unsupported versions can leave gaps. Rules include subdomains. Website time is a subset of browser app time.",
                )
                if (prefs.websiteTracking && !AppBlockService.connected)
                    TextButton(onClick = ::openAccess) { Text("Enable the Accessibility service") }
            }
            Row(verticalAlignment = Alignment.CenterVertically) {
                Text(
                    if (section == 0) "App rules" else "Website rules",
                    Modifier.weight(1f),
                    style = MaterialTheme.typography.titleMedium,
                )
                IconButton(
                    onClick = {
                        guard {
                            if (section == 0) chooser = true
                            else editor = BlockRule("web:", "comfort")
                        }
                    }
                ) {
                    Icon(
                        Icons.Outlined.Add,
                        if (section == 0) "Add app rule" else "Add website rule",
                    )
                }
                IconButton(onClick = { banner = true }) {
                    Icon(Icons.Outlined.Wallpaper, "Customize blocking banner")
                }
            }
            val rules = prefs.blockRules.filter { it.app.startsWith("web:") == (section == 1) }
            if (rules.isEmpty())
                InfoCard(
                    "No rules yet",
                    if (section == 0) "Tap + to choose an app."
                    else "Tap + to add a domain, such as example.com.",
                )
            rules.forEach { rule ->
                key(rule.app) {
                    val web = rule.app.startsWith("web:")
                    val label =
                        if (web) rule.app.removePrefix("web:")
                        else state.installed.firstOrNull { it.app == rule.app }?.label ?: rule.app
                    OutlinedCard(
                        onClick = { guard { editor = rule } },
                        modifier = Modifier.fillMaxWidth(),
                    ) {
                        Column(
                            Modifier.padding(18.dp),
                            verticalArrangement = Arrangement.spacedBy(12.dp),
                        ) {
                            Row(
                                verticalAlignment = Alignment.CenterVertically,
                                horizontalArrangement = Arrangement.spacedBy(12.dp),
                            ) {
                                if (web) Icon(Icons.Outlined.Language, null, Modifier.size(40.dp))
                                else AppIcon(rule.app, label)
                                Column(Modifier.weight(1f)) {
                                    Text(label, fontWeight = FontWeight.SemiBold)
                                    Text(
                                        when (rule.mode) {
                                            "comfort" -> "Comfort · ${rule.allowanceMinutes}m / 24h"
                                            "strict" -> "Strict · no access"
                                            else -> "Previous rule · tap to choose a mode"
                                        },
                                        style = MaterialTheme.typography.bodySmall,
                                    )
                                }
                                Switch(
                                    rule.enabled,
                                    onCheckedChange = { enabled ->
                                        guard {
                                            model.edit {
                                                it.preferences.updateRule(
                                                    rule.copy(enabled = enabled)
                                                )
                                            }
                                        }
                                    },
                                )
                            }
                            if (rule.mode == "comfort") {
                                val used = state.allowances[rule.app]
                                val limit = rule.allowanceMinutes * 60_000L
                                Text(
                                    if (web && !prefs.websiteTracking) "Website tracking is off"
                                    else if (used == null)
                                        "Usage access is needed to measure this allowance"
                                    else
                                        "${duration(used)} used · ${duration((limit-used).coerceAtLeast(0))} remaining",
                                    style = MaterialTheme.typography.bodySmall,
                                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                                )
                                if (used != null)
                                    LinearProgressIndicator(
                                        progress = { (used.toFloat() / limit).coerceIn(0f, 1f) },
                                        modifier = Modifier.fillMaxWidth(),
                                    )
                            }
                        }
                    }
                }
            }
        } else {
            Row(
                Modifier.height(IntrinsicSize.Min),
                horizontalArrangement = Arrangement.spacedBy(12.dp),
            ) {
                Metric(
                    "Today",
                    state.blocksToday.toString(),
                    "Blocked openings",
                    Modifier.weight(1f),
                )
                Metric(
                    "7 days",
                    state.blocksWeek.toString(),
                    "Blocked openings",
                    Modifier.weight(1f),
                )
            }
            if (state.blocks.isEmpty())
                InfoCard(
                    "Nothing blocked yet",
                    "Attempts appear here when a rule covers an app or website.",
                )
            state.blocks.take(60).forEach { event ->
                OutlinedCard(Modifier.fillMaxWidth()) {
                    Row(
                        Modifier.padding(16.dp),
                        horizontalArrangement = Arrangement.spacedBy(12.dp),
                        verticalAlignment = Alignment.CenterVertically,
                    ) {
                        if (event.app.startsWith("web:")) Icon(Icons.Outlined.Language, null)
                        else AppIcon(event.app, event.app, Modifier.size(34.dp))
                        Column(Modifier.weight(1f)) {
                            Text(
                                if (event.app.startsWith("web:")) event.app.removePrefix("web:")
                                else
                                    state.installed.firstOrNull { it.app == event.app }?.label
                                        ?: event.app,
                                fontWeight = FontWeight.Medium,
                            )
                            Text(event.reason, style = MaterialTheme.typography.bodySmall)
                            Text(
                                java.time.Instant.ofEpochMilli(event.at)
                                    .atZone(java.time.ZoneId.systemDefault())
                                    .toLocalDate()
                                    .toString() + " · " + timeLabel(event.at),
                                style = MaterialTheme.typography.labelSmall,
                            )
                        }
                    }
                }
            }
        }
        Text(
            "Strict removes the pause shortcut. Rules can be edited here; a PIN can protect changes. Android always lets the phone owner disable the service.",
            style = MaterialTheme.typography.bodySmall,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
        )
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
            title = { Text("Enable app blocking?") },
            text = {
                Text(
                    "TimeLens uses Android Accessibility to detect app switches and cover selected apps. Website address-bar access is a separate opt-in. Nothing is sent off this phone. Android may ask you to allow restricted settings for a downloaded APK."
                )
            },
            confirmButton = {
                TextButton(
                    onClick = {
                        model.edit { it.preferences.blocksEnabled = true }
                        disclosure = false
                        openAccess()
                    }
                ) {
                    Text("Open Android settings")
                }
            },
            dismissButton = { TextButton(onClick = { disclosure = false }) { Text("Cancel") } },
        )
    if (websites)
        AlertDialog(
            onDismissRequest = { websites = false },
            title = { Text("Allow browser address-bar access?") },
            text = {
                Text(
                    "TimeLens will look up only known browser address-bar controls using Accessibility. It saves hostnames and observed foreground time locally, including private tabs if the browser exposes them. It does not read webpage text, passwords, URL paths or search queries. Hidden address bars and unsupported browsers cannot be tracked or blocked reliably. You can turn this off and delete saved website history."
                )
            },
            confirmButton = {
                TextButton(
                    onClick = {
                        model.edit { it.preferences.websiteTracking = true }
                        websites = false
                    }
                ) {
                    Text("Enable on this phone")
                }
            },
            dismissButton = { TextButton(onClick = { websites = false }) { Text("Cancel") } },
        )
    if (chooser) {
        var search by rememberSaveable { mutableStateOf("") }
        val apps =
            state.installed.filter {
                it.app !in safe &&
                    (it.label.contains(search, true) || it.app.contains(search, true))
            }
        AlertDialog(
            onDismissRequest = { chooser = false },
            title = { Text("Choose an app") },
            text = {
                Column {
                    OutlinedTextField(
                        search,
                        { search = it },
                        singleLine = true,
                        label = { Text("Search apps") },
                        modifier = Modifier.fillMaxWidth(),
                    )
                    Column(Modifier.heightIn(max = 360.dp).verticalScroll(rememberScrollState())) {
                        apps.take(80).forEach { app ->
                            Row(
                                Modifier.fillMaxWidth()
                                    .clickable {
                                        editor =
                                            prefs.blockRules.firstOrNull { it.app == app.app }
                                                ?: BlockRule(app.app, "comfort")
                                        chooser = false
                                    }
                                    .padding(vertical = 12.dp),
                                verticalAlignment = Alignment.CenterVertically,
                                horizontalArrangement = Arrangement.spacedBy(12.dp),
                            ) {
                                AppIcon(app.app, app.label, Modifier.size(36.dp))
                                Text(app.label)
                            }
                        }
                        if (apps.size > 80)
                            Text(
                                "Search to narrow the list",
                                style = MaterialTheme.typography.bodySmall,
                            )
                    }
                }
            },
            confirmButton = { TextButton(onClick = { chooser = false }) { Text("Close") } },
        )
    }
    editor?.let { rule ->
        BlockRuleEditor(
            rule,
            state.installed.firstOrNull { it.app == rule.app }?.label ?: rule.app,
            onDismiss = { editor = null },
            onSave = { updated ->
                model.edit {
                    it.preferences.blockRules =
                        it.preferences.blockRules.filterNot { old ->
                            old.app == rule.app || old.app == updated.app
                        } + updated
                }
                editor = null
            },
            onDelete = {
                model.edit {
                    it.preferences.blockRules =
                        it.preferences.blockRules.filterNot { old -> old.app == rule.app }
                }
                editor = null
            },
        )
    }
    if (banner) {
        var title by rememberSaveable { mutableStateOf(prefs.blockTitle) }
        var message by rememberSaveable { mutableStateOf(prefs.blockMessage) }
        AlertDialog(
            onDismissRequest = { banner = false },
            title = { Text("Blocking banner") },
            text = {
                Column(
                    Modifier.heightIn(max = 440.dp).verticalScroll(rememberScrollState()),
                    verticalArrangement = Arrangement.spacedBy(16.dp),
                ) {
                    Surface(
                        shape = MaterialTheme.shapes.large,
                        color = MaterialTheme.colorScheme.surfaceContainerHigh,
                    ) {
                        Column(
                            Modifier.fillMaxWidth().padding(24.dp),
                            horizontalAlignment = Alignment.CenterHorizontally,
                            verticalArrangement = Arrangement.spacedBy(12.dp),
                        ) {
                            Icon(Icons.Outlined.Shield, null, Modifier.size(40.dp))
                            Text(title, style = MaterialTheme.typography.titleLarge)
                            Text(message.replace("{app}", "Instagram"))
                        }
                    }
                    OutlinedTextField(
                        title,
                        { title = it.take(100) },
                        label = { Text("Title") },
                        modifier = Modifier.fillMaxWidth(),
                    )
                    OutlinedTextField(
                        message,
                        { message = it.take(500) },
                        label = { Text("Message · {app} inserts the app or site") },
                        modifier = Modifier.fillMaxWidth(),
                        minLines = 2,
                    )
                }
            },
            confirmButton = {
                TextButton(
                    onClick = {
                        guard {
                            model.edit {
                                it.preferences.blockTitle = title
                                it.preferences.blockMessage = message
                            }
                            banner = false
                        }
                    }
                ) {
                    Text("Save banner")
                }
            },
            dismissButton = { TextButton(onClick = { banner = false }) { Text("Cancel") } },
        )
    }
}

@Composable
private fun BlockRuleEditor(
    rule: BlockRule,
    label: String,
    onDismiss: () -> Unit,
    onSave: (BlockRule) -> Unit,
    onDelete: () -> Unit,
) {
    val web = rule.app.startsWith("web:")
    var domain by rememberSaveable(rule.app) { mutableStateOf(rule.app.removePrefix("web:")) }
    var mode by
        rememberSaveable(rule.app) {
            mutableStateOf(
                if (rule.mode == "strict" || rule.mode == "always") "strict" else "comfort"
            )
        }
    var minutes by rememberSaveable(rule.app) { mutableStateOf(rule.allowanceMinutes.toString()) }
    val host = WebAddress.host(domain)
    val valid =
        (!web || host != null) &&
            (mode == "strict" || minutes.toIntOrNull()?.let { it in 1..1440 } == true)
    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(if (web) "Website rule" else label) },
        text = {
            Column(
                Modifier.heightIn(max = 450.dp).verticalScroll(rememberScrollState()),
                verticalArrangement = Arrangement.spacedBy(12.dp),
            ) {
                if (web)
                    OutlinedTextField(
                        domain,
                        { domain = it },
                        singleLine = true,
                        label = { Text("Domain · example.com") },
                        modifier = Modifier.fillMaxWidth(),
                        isError = domain.isNotEmpty() && host == null,
                    )
                Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    FilterChip(
                        mode == "comfort",
                        onClick = { mode = "comfort" },
                        label = { Text("Comfort") },
                        leadingIcon = { Icon(Icons.Outlined.Spa, null, Modifier.size(18.dp)) },
                    )
                    FilterChip(
                        mode == "strict",
                        onClick = { mode = "strict" },
                        label = { Text("Strict") },
                        leadingIcon = { Icon(Icons.Outlined.Lock, null, Modifier.size(18.dp)) },
                    )
                }
                Text(
                    if (mode == "strict")
                        "Always blocked while this rule and blocking are enabled. There is no pause button on the banner."
                    else
                        "A rolling 24-hour allowance. Time becomes available as earlier use leaves the 24-hour window.",
                    style = MaterialTheme.typography.bodyMedium,
                )
                if (mode == "comfort")
                    OutlinedTextField(
                        minutes,
                        { minutes = it.filter(Char::isDigit).take(4) },
                        label = { Text("Allowed minutes · 1–1440") },
                        keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                        singleLine = true,
                        modifier = Modifier.fillMaxWidth(),
                    )
                if (web)
                    Text(
                        "Includes this domain and its subdomains. Measures only browser time that TimeLens can observe.",
                        style = MaterialTheme.typography.bodySmall,
                    )
                TextButton(onClick = onDelete) {
                    Icon(Icons.Outlined.DeleteOutline, null)
                    Spacer(Modifier.width(8.dp))
                    Text("Remove rule")
                }
            }
        },
        confirmButton = {
            TextButton(
                enabled = valid,
                onClick = {
                    onSave(
                        rule.copy(
                            app = if (web) "web:$host" else rule.app,
                            mode = mode,
                            enabled = true,
                            allowanceMinutes = minutes.toIntOrNull()?.coerceIn(1, 1440) ?: 30,
                        )
                    )
                },
            ) {
                Text("Save rule")
            }
        },
        dismissButton = { TextButton(onClick = onDismiss) { Text("Cancel") } },
    )
}
