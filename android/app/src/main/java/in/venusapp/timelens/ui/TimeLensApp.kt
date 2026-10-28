package `in`.venusapp.timelens.ui

import android.content.ActivityNotFoundException
import android.content.Intent
import android.net.Uri
import android.provider.Settings
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.outlined.List
import androidx.compose.material.icons.outlined.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.platform.LocalWindowInfo
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.compose.LifecycleEventEffect
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.lifecycle.viewmodel.compose.viewModel
import `in`.venusapp.timelens.BuildConfig
import `in`.venusapp.timelens.data.*
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import java.time.Instant
import java.time.LocalDate
import java.time.ZoneId
import java.time.format.DateTimeFormatter

private val Accent = Color(0xFFC8E86A)
private val Dark = darkColorScheme(primary = Accent, onPrimary = Color(0xFF151E00), primaryContainer = Color(0xFF253412), onPrimaryContainer = Color(0xFFD8EFAC), secondaryContainer = Color(0xFF293322), onSecondaryContainer = Color(0xFFD8E4CD), background = Color(0xFF0D0F0A), surface = Color(0xFF141810), surfaceContainer = Color(0xFF1C2118))
private val Light = lightColorScheme(primary = Color(0xFF486744), primaryContainer = Color(0xFFDFECCB), onPrimaryContainer = Color(0xFF243B22), secondaryContainer = Color(0xFFE7E9DC), onSecondaryContainer = Color(0xFF394436), surfaceContainer = Color(0xFFEEECE1), background = Color(0xFFF4F1E8), surface = Color(0xFFFCFAF4))
private data class Tab(val label: String, val icon: ImageVector)
private val Tabs = listOf(Tab("Today", Icons.Outlined.Today), Tab("Apps", Icons.Outlined.Apps), Tab("Timeline", Icons.AutoMirrored.Outlined.List), Tab("Focus", Icons.Outlined.Timer), Tab("Settings", Icons.Outlined.Settings))
fun duration(ms: Long): String {
    val seconds = ms.coerceAtLeast(0) / 1000
    val minutes = seconds / 60
    return when { minutes >= 60 -> "${minutes / 60}h ${minutes % 60}m"; minutes > 0 -> "${minutes}m"; seconds > 0 -> "<1m"; else -> "0m" }
}
internal fun clock(ms: Long) = Instant.ofEpochMilli(ms).atZone(ZoneId.systemDefault()).format(DateTimeFormatter.ofPattern("HH:mm"))

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun TimeLensApp(model: AppModel = viewModel()) {
    val state by model.state.collectAsStateWithLifecycle()
    val prefs = model.preferences
    var theme by remember { mutableStateOf(prefs.theme) }
    val dark = theme == "dark" || (theme == "system" && isSystemInDarkTheme())
    MaterialTheme(colorScheme = if (dark) Dark else Light) {
        var tab by rememberSaveable { mutableIntStateOf(0) }
        val wide = with(LocalDensity.current) { LocalWindowInfo.current.containerSize.width.toDp() >= 600.dp }
        val context = LocalContext.current
        val scope = rememberCoroutineScope()
        val snackbar = remember { SnackbarHostState() }
        var exportDay by remember { mutableStateOf<DayUsage?>(null) }
        var exportJson by remember { mutableStateOf(true) }
        val export = rememberLauncherForActivityResult(ActivityResultContracts.CreateDocument("*/*")) { uri ->
            val day = exportDay
            if (uri != null && day != null) scope.launch {
                val success = withContext(Dispatchers.IO) {
                    runCatching {
                        context.contentResolver.openOutputStream(uri, "wt")?.bufferedWriter()?.use { it.write(if (exportJson) Export.json(day) else Export.csv(day)) } ?: error("No output stream")
                    }.isSuccess
                }
                snackbar.showSnackbar(if (success) "Activity exported" else "Export failed. Please choose another file and try again.")
            }
        }
        fun openAccess() {
            try { context.startActivity(Intent(Settings.ACTION_USAGE_ACCESS_SETTINGS, Uri.parse("package:${context.packageName}"))) }
            catch (_: ActivityNotFoundException) {
                try { context.startActivity(Intent(Settings.ACTION_USAGE_ACCESS_SETTINGS)) }
                catch (_: ActivityNotFoundException) { scope.launch { snackbar.showSnackbar("Open Android Settings → Special app access → Usage access → TimeLens.") } }
            }
        }
        LifecycleEventEffect(Lifecycle.Event.ON_RESUME) { model.refresh() }
        Scaffold(
            snackbarHost = { SnackbarHost(snackbar) },
            topBar = { TopAppBar(title = { Column { Text("TimeLens", fontWeight = FontWeight.Bold); Text("YOUR TIME, ON YOUR DEVICE", style = MaterialTheme.typography.labelSmall, color = MaterialTheme.colorScheme.primary) } }, actions = {
                IconButton(onClick = { model.refresh() }, enabled = !state.loading) { Icon(Icons.Outlined.Refresh, "Refresh activity") }
            }) },
            bottomBar = { if (!wide) NavigationBar { Tabs.forEachIndexed { index, item -> NavigationBarItem(selected = tab == index, onClick = { tab = index }, icon = { Icon(item.icon, null) }, label = { Text(item.label, maxLines = 1) }) } } }
        ) { padding ->
            Row(Modifier.fillMaxSize().padding(padding)) {
                if (wide) NavigationRail { Tabs.forEachIndexed { index, item -> NavigationRailItem(selected = tab == index, onClick = { tab = index }, icon = { Icon(item.icon, null) }, label = { Text(item.label) }) } }
                LazyColumn(Modifier.weight(1f).fillMaxHeight(), contentPadding = PaddingValues(20.dp), verticalArrangement = Arrangement.spacedBy(16.dp)) {
                    if (state.loading) item { LinearProgressIndicator(Modifier.fillMaxWidth()) }
                    state.error?.let { message -> item { InfoCard("Something needs attention", message) } }
                    if (tab in 0..2) {
                        item { DateSelector(state.date, onDate = model::refresh) }
                        if (state.day?.hasAccess == false) item { PermissionCard(::openAccess) }
                    }
                    when (tab) {
                        0 -> {
                            item { Text(if (state.date == LocalDate.now()) "A little perspective." else "Your day in perspective.", style = MaterialTheme.typography.headlineMedium, fontWeight = FontWeight.Bold) }
                            state.day?.let { day ->
                                item { SummaryCard(day, prefs.budget) }
                                item { InfoCard("Categories", if (day.apps.isEmpty()) "Your categories will appear after activity is available." else day.apps.groupBy { it.category }.entries.sortedByDescending { it.value.sumOf { row -> row.milliseconds } }.joinToString("\n") { (category, rows) -> "$category · ${duration(rows.sumOf { it.milliseconds })}" }) }
                                item { SectionTitle("Most used", "Tap an app to set its category or a daily budget.") }
                                items(day.apps.take(5), key = { it.app }) { app -> AppCard(app, model) }
                                if (day.sessions.isEmpty()) item { EmptyActivity() }
                                item { OutlinedButton(onClick = { tab = 1 }, modifier = Modifier.fillMaxWidth()) { Text("Explore all apps") } }
                            }
                        }
                        1 -> {
                            item { SectionTitle("Your apps", "Local foreground activity for the selected day.") }
                            item { AppsFilter(state.day, model) }
                        }
                        2 -> {
                            item { SectionTitle("Timeline", "Most recent first. Screen-off time and gaps are excluded.") }
                            state.day?.let { day ->
                                if (day.sessions.isEmpty()) item { EmptyActivity() }
                                val labels = day.apps.associate { it.app to it.label }
                                items(day.sessions.asReversed(), key = { "${it.app}:${it.start}" }) { session ->
                                    OutlinedCard(Modifier.fillMaxWidth()) { Row(Modifier.padding(16.dp), horizontalArrangement = Arrangement.spacedBy(16.dp)) {
                                        Icon(Icons.Outlined.Circle, null, tint = MaterialTheme.colorScheme.primary, modifier = Modifier.size(16.dp))
                                        Column(Modifier.weight(1f)) { Text(labels[session.app] ?: session.app, fontWeight = FontWeight.SemiBold); Text("${clock(session.start)} – ${clock(session.end)}", style = MaterialTheme.typography.bodySmall) }
                                        Text(duration(session.milliseconds))
                                    } }
                                }
                            }
                        }
                        3 -> item { FocusScreen(prefs, state.revision) }
                        4 -> item { SettingsScreen(model, theme, onTheme = { theme = it; prefs.theme = it }, onAccess = ::openAccess, onExport = { json ->
                            state.day?.let { exportDay = it; exportJson = json; export.launch("TimeLens-${it.date}.${if (json) "json" else "csv"}") }
                        }, exportEnabled = state.day != null && !state.loading) }
                    }
                    item { Text("Private by design · Android preview ${BuildConfig.VERSION_NAME}", style = MaterialTheme.typography.labelSmall, color = MaterialTheme.colorScheme.onSurfaceVariant) }
                }
            }
        }
    }
}
