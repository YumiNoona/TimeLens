package `in`.venusapp.timelens.ui

import android.content.Intent
import android.provider.Settings
import androidx.activity.compose.BackHandler
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.*
import androidx.compose.material.icons.outlined.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.saveable.rememberSaveableStateHolder
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.platform.*
import androidx.compose.ui.unit.dp
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.compose.LifecycleEventEffect
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.lifecycle.viewmodel.compose.viewModel
import `in`.venusapp.timelens.BuildConfig
import `in`.venusapp.timelens.data.*
import `in`.venusapp.timelens.domain.ThemeMode
import java.io.File
import java.time.Instant
import java.time.ZoneId
import java.time.format.DateTimeFormatter
import java.util.UUID
import kotlinx.coroutines.*

private data class Page(val title: String, val icon: ImageVector, val selectedIcon: ImageVector)

private val Pages =
    listOf(
        Page("Today", Icons.Outlined.Today, Icons.Filled.Today),
        Page("Apps", Icons.Outlined.Widgets, Icons.Filled.Widgets),
        Page("History", Icons.Outlined.Assessment, Icons.Filled.Assessment),
        Page("Block", Icons.Outlined.Shield, Icons.Filled.Shield),
        Page("Settings", Icons.Outlined.Settings, Icons.Filled.Settings),
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
    // Preferences are not Snapshot state: explicitly observe the edit revision at this scope.
    val theme = remember(state.revision) { prefs.theme }
    val display = remember(state.revision) {
        DisplayOptions(prefs.showSeconds, prefs.use24Hour, prefs.compact, prefs.grayIcons)
    }
    val dark = ThemeMode.isDark(theme, isSystemInDarkTheme())
    val scheme = animatedMonoScheme(dark)
    val pageStates = rememberSaveableStateHolder()
    var historyDate by rememberSaveable { mutableStateOf(java.time.LocalDate.now().toString()) }
    val focusManager = LocalFocusManager.current
    fun navigate(index: Int) {
        focusManager.clearFocus()
        if (page == 2) historyDate = state.date.toString()
        page = index
        val date =
            if (index == 2) java.time.LocalDate.parse(historyDate) else java.time.LocalDate.now()
        if (state.date != date || index == 3) model.refresh(date)
    }
    BackHandler(page != 0) { navigate(0) }
    MaterialTheme(
        colorScheme = scheme,
        typography = TimeLensTypography,
        shapes =
            Shapes(
                medium = androidx.compose.foundation.shape.RoundedCornerShape(18.dp),
                large = androidx.compose.foundation.shape.RoundedCornerShape(24.dp),
            ),
    ) {
        CompositionLocalProvider(
            LocalDisplay provides
                display
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
            LifecycleEventEffect(Lifecycle.Event.ON_RESUME) {
                model.refresh(if (page == 2) state.date else java.time.LocalDate.now())
            }
            val lifecycle = androidx.lifecycle.compose.LocalLifecycleOwner.current.lifecycle
            val lifecycleState by lifecycle.currentStateFlow.collectAsState()
            LaunchedEffect(lifecycleState, page) {
                if (lifecycleState.isAtLeast(Lifecycle.State.RESUMED) && page in 0..3)
                    while (true) {
                        delay(30_000)
                        // History is immutable while browsing a past date. Today follows midnight.
                        if (page != 2 || model.state.value.date == java.time.LocalDate.now())
                            model.refresh(java.time.LocalDate.now())
                    }
            }
            Scaffold(
                snackbarHost = { SnackbarHost(snackbar) },
                topBar = {
                    TopAppBar(
                        title = {
                            Text(Pages[page].title, style = MaterialTheme.typography.titleLarge)
                        },
                        actions = {
                            IconButton(onClick = {
                                model.edit(refreshActivity = false) {
                                    it.preferences.theme = if (dark) "light" else "dark"
                                }
                            }) {
                                Icon(if (dark) Icons.Outlined.LightMode else Icons.Outlined.DarkMode,
                                    if (dark) "Switch to light mode" else "Switch to dark mode")
                            }
                        },
                    )
                },
                bottomBar = {
                    if (!wide)
                        NavigationBar(containerColor = scheme.surface) {
                            Pages.forEachIndexed { index, item ->
                                NavigationBarItem(
                                    selected = page == index,
                                    onClick = { navigate(index) },
                                    icon = { Icon(if (page == index) item.selectedIcon else item.icon, item.title) },
                                    colors = NavigationBarItemDefaults.colors(
                                        indicatorColor = androidx.compose.ui.graphics.Color.Transparent,
                                        selectedIconColor = scheme.onSurface,
                                        unselectedIconColor = scheme.onSurfaceVariant,
                                    ),
                                    alwaysShowLabel = false,
                                )
                            }
                        }
                },
            ) { padding ->
                Row(Modifier.fillMaxSize().padding(padding)) {
                    if (wide)
                        NavigationRail {
                            Pages.forEachIndexed { index, item ->
                                NavigationRailItem(
                                    selected = page == index,
                                    onClick = { navigate(index) },
                                    icon = { Icon(if (page == index) item.selectedIcon else item.icon, null) },
                                    colors = NavigationRailItemDefaults.colors(
                                        indicatorColor = androidx.compose.ui.graphics.Color.Transparent,
                                    ),
                                    label = { Text(item.title) },
                                )
                            }
                        }
                    BoxWithConstraints(Modifier.weight(1f).fillMaxHeight()) {
                        val contentWidth = minOf(maxWidth, 1100.dp)
                        // A page owns both its scroll offset and its saveable controls. Refresh
                        // never
                        // inserts an item before this content or changes its composition identity.
                        pageStates.SaveableStateProvider(page) {
                            if (page == 1) {
                                AppsScreen(state, model,
                                    modifier = Modifier.width(contentWidth).fillMaxHeight()
                                        .testTag("page-1").align(androidx.compose.ui.Alignment.TopCenter),
                                    onAccess = { openSetting(Settings.ACTION_USAGE_ACCESS_SETTINGS) })
                            } else
                            Column(
                                Modifier.width(contentWidth)
                                    .fillMaxHeight()
                                    .testTag("page-$page")
                                    .align(androidx.compose.ui.Alignment.TopCenter)
                                    .verticalScroll(rememberScrollState())
                                    .padding(if (prefs.compact) 14.dp else 20.dp),
                                verticalArrangement =
                                    Arrangement.spacedBy(if (prefs.compact) 12.dp else 18.dp),
                            ) {
                                state.error?.let { InfoCard("Could not refresh", it) }
                                if (page == 2)
                                    DateSelector(state.date) {
                                        historyDate = it.toString()
                                        model.refresh(it)
                                    }
                                if (page in 0..2 && state.day?.hasAccess == false)
                                    PermissionCard {
                                        openSetting(Settings.ACTION_USAGE_ACCESS_SETTINGS)
                                    }
                                when (page) {
                                    0 ->
                                        TodayScreen(
                                            state,
                                            model,
                                            onApps = { navigate(1) },
                                            onFocus = { navigate(3) },
                                        )
                                    2 -> HistoryScreen(state, model)
                                    3 -> BlockScreen(model)
                                    4 ->
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
                                Text(
                                    "ON THIS PHONE · ANDROID " + BuildConfig.VERSION_NAME,
                                    style = MaterialTheme.typography.labelSmall,
                                    color = scheme.onSurfaceVariant,
                                    modifier = Modifier.padding(vertical = 12.dp),
                                )
                            }
                        }
                        if (state.loading)
                            LinearProgressIndicator(
                                Modifier.fillMaxWidth()
                                    .align(androidx.compose.ui.Alignment.TopCenter)
                            )
                    }
                }
            }
        }
    }
}
