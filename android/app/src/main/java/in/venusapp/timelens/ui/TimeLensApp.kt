package `in`.venusapp.timelens.ui

import android.content.Intent
import android.provider.Settings
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.outlined.List
import androidx.compose.material.icons.outlined.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.platform.*
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.compose.LifecycleEventEffect
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.lifecycle.viewmodel.compose.viewModel
import `in`.venusapp.timelens.BuildConfig
import `in`.venusapp.timelens.data.*
import java.io.File
import java.time.Instant
import java.time.ZoneId
import java.time.format.DateTimeFormatter
import java.util.UUID
import kotlinx.coroutines.*

private data class Page(val title: String, val icon: ImageVector)

private val Pages =
    listOf(
        Page("Today", Icons.Outlined.Today),
        Page("Apps", Icons.Outlined.Apps),
        Page("Reports", Icons.Outlined.BarChart),
        Page("Focus", Icons.Outlined.Shield),
        Page("Timeline", Icons.AutoMirrored.Outlined.List),
        Page("Settings", Icons.Outlined.Settings),
    )

fun duration(ms: Long, precise: Boolean = false): String {
    val seconds = ms.coerceAtLeast(0) / 1000
    val minutes = seconds / 60
    if (precise) return "${if(minutes>=60) "${minutes/60}h " else ""}${minutes%60}m ${seconds%60}s"
    return when {
        minutes >= 60 -> "${minutes/60}h ${minutes%60}m"
        minutes > 0 -> "${minutes}m"
        seconds > 0 -> "<1m"
        else -> "0m"
    }
}

internal fun clock(ms: Long) =
    Instant.ofEpochMilli(ms)
        .atZone(ZoneId.systemDefault())
        .format(DateTimeFormatter.ofPattern("HH:mm"))

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun TimeLensApp(model: AppModel = viewModel()) {
    val state by model.state.collectAsStateWithLifecycle()
    val prefs = model.preferences
    var page by rememberSaveable { mutableIntStateOf(0) }
    var more by remember { mutableStateOf(false) }
    val dark =
        when (prefs.theme) {
            "light" -> false
            "system" -> isSystemInDarkTheme()
            else -> true
        }
    val accent = AccentColors[prefs.accent] ?: AccentColors.values.first()
    val scheme =
        if (dark)
            darkColorScheme(
                primary = accent,
                onPrimary = Color(0xFF142000),
                primaryContainer = accent.copy(alpha = .13f),
                onPrimaryContainer = accent,
                secondaryContainer = Color(0xFF293024),
                background = Color(0xFF0D100C),
                surface = Color(0xFF151A13),
                surfaceContainer = Color(0xFF1B2118),
                surfaceContainerHigh = Color(0xFF242C20),
                surfaceContainerHighest = Color(0xFF2D3529),
            )
        else
            lightColorScheme(
                primary = Color(accent.red * .45f, accent.green * .45f, accent.blue * .45f),
                primaryContainer =
                    Color(
                        .82f + .16f * accent.red,
                        .82f + .16f * accent.green,
                        .82f + .16f * accent.blue,
                    ),
                onPrimaryContainer =
                    Color(accent.red * .25f, accent.green * .25f, accent.blue * .25f),
                secondaryContainer = Color(0xFFE7E9DC),
                surfaceContainer = Color(0xFFEEECE1),
                surfaceContainerHigh = Color(0xFFE6E7DC),
                background = Color(0xFFF4F1E8),
                surface = Color(0xFFFCFAF4),
            )
    MaterialTheme(
        colorScheme = scheme,
        shapes =
            Shapes(
                medium = androidx.compose.foundation.shape.RoundedCornerShape(18.dp),
                large = androidx.compose.foundation.shape.RoundedCornerShape(24.dp),
            ),
    ) {
        CompositionLocalProvider(
            LocalDisplay provides DisplayOptions(prefs.showSeconds, prefs.use24Hour, prefs.compact)
        ) {
            val wide =
                with(LocalDensity.current) {
                    LocalWindowInfo.current.containerSize.width.toDp() >= 700.dp
                }
            val context = LocalContext.current
            LaunchedEffect(dark) {
                val activity =
                    generateSequence(context as android.content.Context?) {
                            (it as? android.content.ContextWrapper)?.baseContext
                        }
                        .filterIsInstance<android.app.Activity>()
                        .firstOrNull()
                activity?.window?.let { window ->
                    androidx.core.view.WindowCompat.getInsetsController(window, window.decorView)
                        .apply {
                            isAppearanceLightStatusBars = !dark
                            isAppearanceLightNavigationBars = !dark
                        }
                }
            }
            val scope = rememberCoroutineScope()
            val snackbar = remember { SnackbarHostState() }
            var pendingExport by rememberSaveable { mutableStateOf("") }
            var importRows by remember {
                mutableStateOf<List<`in`.venusapp.timelens.domain.Session>?>(null)
            }
            val importer =
                rememberLauncherForActivityResult(ActivityResultContracts.OpenDocument()) { uri ->
                    if (uri != null)
                        scope.launch {
                            try {
                                importRows =
                                    withContext(Dispatchers.IO) {
                                        context.contentResolver.openInputStream(uri)?.use {
                                            Import.parse(Import.read(it))
                                        } ?: error("Cannot open this file")
                                    }
                            } catch (cancelled: CancellationException) {
                                throw cancelled
                            } catch (error: Exception) {
                                snackbar.showSnackbar(
                                    error.message?.take(150) ?: "This file could not be read."
                                )
                            }
                        }
                }
            val export =
                rememberLauncherForActivityResult(ActivityResultContracts.CreateDocument("*/*")) {
                    uri ->
                    val snapshot =
                        pendingExport.takeIf { it.isNotEmpty() }?.let { File(context.cacheDir, it) }
                    pendingExport = ""
                    if (uri != null && snapshot != null)
                        scope.launch {
                            val success =
                                withContext(Dispatchers.IO) {
                                    runCatching {
                                        context.contentResolver.openOutputStream(uri, "wt")?.use {
                                            output ->
                                            snapshot.inputStream().use { it.copyTo(output) }
                                        } ?: error("No output stream")
                                    }
                                        .also { snapshot.delete() }
                                        .isSuccess
                                }
                            snackbar.showSnackbar(
                                if (success) "Activity exported"
                                else "Export failed. Please choose another file."
                            )
                        }
                    else snapshot?.delete()
                }
            fun openSetting(action: String) {
                runCatching { context.startActivity(Intent(action)) }
                    .onFailure {
                        scope.launch {
                            snackbar.showSnackbar(
                                "Open this permission from Android Settings → Apps → TimeLens."
                            )
                        }
                    }
            }
            fun saveExport(json: Boolean, range: Int) {
                val day = state.day ?: return
                scope.launch {
                    val filename = "export-${UUID.randomUUID()}"
                    val snapshot = File(context.cacheDir, filename)
                    try {
                        withContext(Dispatchers.IO) {
                            val selected =
                                if (range == 1) listOf(day)
                                else
                                    UsageRepository(context, prefs)
                                        .use { repo ->
                                            (0 until range).map {
                                                repo.load(state.date.minusDays(it.toLong()))
                                            }
                                        }
                                        .asReversed()
                            snapshot.writeText(
                                if (json) Export.jsonRange(selected) else Export.csvRange(selected)
                            )
                        }
                        pendingExport = filename
                        export.launch(
                            "TimeLens-${day.date}-${range}d.${if(json) "json" else "csv"}"
                        )
                    } catch (cancelled: CancellationException) {
                        snapshot.delete()
                        throw cancelled
                    } catch (_: Exception) {
                        snapshot.delete()
                        pendingExport = ""
                        snackbar.showSnackbar("Could not prepare the export. Try again.")
                    }
                }
            }
            importRows?.let { rows ->
                AlertDialog(
                    onDismissRequest = { importRows = null },
                    title = { Text("Restore Android activity?") },
                    text = {
                        Text(
                            "Restore " +
                                rows.size +
                                " intervals from " +
                                java.time.Instant.ofEpochMilli(rows.first().start)
                                    .atZone(java.time.ZoneId.systemDefault())
                                    .toLocalDate() +
                                ". Imported intervals replace overlapping local activity, while unrelated activity and exclusions are kept. Android may refresh recent dates from this phone’s system history."
                        )
                    },
                    confirmButton = {
                        TextButton(
                            onClick = {
                                model.edit { it.restoreActivity(rows) }
                                importRows = null
                            }
                        ) {
                            Text("Restore activity")
                        }
                    },
                    dismissButton = {
                        TextButton(onClick = { importRows = null }) { Text("Cancel") }
                    },
                )
            }
            LifecycleEventEffect(Lifecycle.Event.ON_RESUME) { model.refresh() }
            val lifecycle = androidx.lifecycle.compose.LocalLifecycleOwner.current.lifecycle
            val lifecycleState by lifecycle.currentStateFlow.collectAsState()
            LaunchedEffect(lifecycleState) {
                if (lifecycleState.isAtLeast(Lifecycle.State.RESUMED))
                    while (true) {
                        delay(30_000)
                        model.refresh()
                    }
            }
            Scaffold(
                snackbarHost = { SnackbarHost(snackbar) },
                topBar = {
                    TopAppBar(
                        title = {
                            Column {
                                Text("TimeLens", fontWeight = FontWeight.Bold)
                                Text(
                                    "${Pages[page].title.uppercase()}  /  YOUR DIGITAL WELLBEING",
                                    style = MaterialTheme.typography.labelSmall,
                                    color = scheme.primary,
                                )
                            }
                        },
                        actions = {
                            IconButton(onClick = { model.archiveNow() }, enabled = !state.loading) {
                                Icon(Icons.Outlined.Refresh, "Refresh activity")
                            }
                            IconButton(onClick = { page = 5 }) {
                                Icon(Icons.Outlined.Tune, "Open settings")
                            }
                        },
                    )
                },
                bottomBar = {
                    if (!wide)
                        NavigationBar(containerColor = scheme.surface) {
                            Pages.take(4).forEachIndexed { index, item ->
                                NavigationBarItem(
                                    selected = page == index,
                                    onClick = { page = index },
                                    icon = { Icon(item.icon, null) },
                                    label = { Text(item.title) },
                                )
                            }
                            NavigationBarItem(
                                selected = page >= 4,
                                onClick = { more = true },
                                icon = { Icon(Icons.Outlined.MoreHoriz, null) },
                                label = { Text("More") },
                            )
                        }
                },
            ) { padding ->
                Row(Modifier.fillMaxSize().padding(padding)) {
                    if (wide)
                        NavigationRail {
                            Pages.forEachIndexed { index, item ->
                                NavigationRailItem(
                                    selected = page == index,
                                    onClick = { page = index },
                                    icon = { Icon(item.icon, null) },
                                    label = { Text(item.title) },
                                )
                            }
                        }
                    BoxWithConstraints(Modifier.weight(1f).fillMaxHeight()) {
                        val contentWidth = minOf(maxWidth, 1100.dp)
                        LazyColumn(
                            Modifier.width(contentWidth)
                                .fillMaxHeight()
                                .align(androidx.compose.ui.Alignment.TopCenter),
                            contentPadding = PaddingValues(if (prefs.compact) 14.dp else 20.dp),
                            verticalArrangement =
                                Arrangement.spacedBy(if (prefs.compact) 12.dp else 18.dp),
                        ) {
                            if (state.loading)
                                item { LinearProgressIndicator(Modifier.fillMaxWidth()) }
                            state.error?.let { message ->
                                item { InfoCard("Could not refresh", message) }
                            }
                            if (page in listOf(0, 1, 2, 4)) {
                                item { DateSelector(state.date, model::refresh) }
                                if (state.day?.hasAccess == false)
                                    item {
                                        PermissionCard {
                                            openSetting(Settings.ACTION_USAGE_ACCESS_SETTINGS)
                                        }
                                    }
                            }
                            when (page) {
                                0 ->
                                    item {
                                        TodayScreen(
                                            state,
                                            model,
                                            onApps = { page = 1 },
                                            onFocus = { page = 3 },
                                        )
                                    }
                                1 -> item { AppsScreen(state, model) }
                                2 -> item { ReportsScreen(state, model) }
                                3 -> item { FocusScreen(model) }
                                4 -> item { TimelineScreen(state, model) }
                                5 ->
                                    item {
                                        SettingsScreen(
                                            model,
                                            onAccess = {
                                                openSetting(Settings.ACTION_USAGE_ACCESS_SETTINGS)
                                            },
                                            onExport = ::saveExport,
                                            onImport = {
                                                importer.launch(
                                                    arrayOf(
                                                        "application/json",
                                                        "text/plain",
                                                        "application/octet-stream",
                                                    )
                                                )
                                            },
                                            exportEnabled =
                                                state.day != null &&
                                                    !state.loading &&
                                                    pendingExport.isEmpty(),
                                        )
                                    }
                            }
                            item {
                                Text(
                                    "ON-DEVICE ONLY · ANDROID ${BuildConfig.VERSION_NAME}",
                                    style = MaterialTheme.typography.labelSmall,
                                    color = scheme.onSurfaceVariant,
                                    modifier = Modifier.padding(vertical = 12.dp),
                                )
                            }
                        }
                    }
                }
            }
            if (more)
                ModalBottomSheet(onDismissRequest = { more = false }) {
                    Column(
                        Modifier.padding(20.dp),
                        verticalArrangement = Arrangement.spacedBy(12.dp),
                    ) {
                        Text(
                            "Make TimeLens yours",
                            style = MaterialTheme.typography.headlineSmall,
                            fontWeight = FontWeight.Bold,
                        )
                        Pages.drop(4).forEachIndexed { offset, item ->
                            OutlinedButton(
                                onClick = {
                                    page = offset + 4
                                    more = false
                                },
                                modifier = Modifier.fillMaxWidth(),
                            ) {
                                Icon(item.icon, null)
                                Spacer(Modifier.width(12.dp))
                                Text(item.title)
                            }
                        }
                        Text(
                            "Reports, blocking and every preference stay on this phone.",
                            style = MaterialTheme.typography.bodySmall,
                            color = scheme.onSurfaceVariant,
                        )
                        Spacer(Modifier.height(20.dp))
                    }
                }
        }
    }
}
