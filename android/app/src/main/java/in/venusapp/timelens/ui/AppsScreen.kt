package `in`.venusapp.timelens.ui

import android.content.Intent
import android.net.Uri
import android.provider.Settings
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
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
import `in`.venusapp.timelens.data.*

@Composable
internal fun AppsScreen(state: UiState, model: AppModel, modifier: Modifier = Modifier, onAccess: () -> Unit = {}) {
    var showSearch by rememberSaveable { mutableStateOf(false) }
    var filters by rememberSaveable { mutableStateOf(false) }
    val focusManager = androidx.compose.ui.platform.LocalFocusManager.current
    var search by rememberSaveable { mutableStateOf("") }
    var category by rememberSaveable { mutableStateOf("All") }
    var installed by rememberSaveable { mutableStateOf(false) }
    var sort by rememberSaveable { mutableStateOf("time") }
    val usage = remember(state.day?.apps) { state.day?.apps.orEmpty().associateBy { it.app } }
    val source = remember(installed, state.installed, usage, state.revision) {
        if (installed)
            state.installed.map { app ->
                usage[app.app]
                    ?: AppUsage(
                        app.app,
                        app.label,
                        0,
                        0,
                        model.preferences.category(app.app),
                        model.preferences.limit(app.app),
                    )
            }
        else usage.values.toList()
    }
    val apps = remember(source, category, search, sort) {
        source
            .filter {
                (category == "All" || it.category == category) &&
                    (it.label.contains(search, true) || it.app.contains(search, true))
            }
            .let { rows ->
                when (sort) {
                    "name" -> rows.sortedBy { it.label.lowercase() }
                    "visits" -> rows.sortedByDescending { it.sessions }
                    else -> rows.sortedByDescending { it.milliseconds }
                }
            }
    }
    LazyColumn(
        modifier = modifier,
        contentPadding = PaddingValues(if (LocalDisplay.current.compact) 14.dp else 20.dp),
        verticalArrangement = Arrangement.spacedBy(14.dp),
    ) {
        item(key = "controls") {
            Column(verticalArrangement = Arrangement.spacedBy(14.dp)) {
                state.error?.let { InfoCard("Could not refresh", it) }
                if (state.day?.hasAccess == false) PermissionCard(onAccess)
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Column(Modifier.weight(1f)) {
                        Text("Your apps", style = MaterialTheme.typography.headlineSmall)
                        Text(
                            if (installed) "Installed on this phone" else "Used today",
                            style = MaterialTheme.typography.bodySmall,
                            color = MaterialTheme.colorScheme.onSurfaceVariant,
                        )
                    }
                    IconButton(
                        onClick = {
                            showSearch = !showSearch
                            if (!showSearch) focusManager.clearFocus()
                        }
                    ) {
                        Icon(
                            if (showSearch) Icons.Outlined.SearchOff else Icons.Outlined.Search,
                            "Search apps",
                        )
                    }
                    IconButton(
                        onClick = {
                            focusManager.clearFocus()
                            filters = true
                        }
                    ) {
                        BadgedBox(
                            badge = { if (category != "All" || sort != "time" || installed) Badge() }
                        ) {
                            Icon(Icons.Outlined.FilterList, "Filter apps")
                        }
                    }
                }
                if (showSearch)
                    OutlinedTextField(
                        search,
                        {
                            search = it
                        },
                        label = { Text("Search name or package") },
                        leadingIcon = { Icon(Icons.Outlined.Search, null) },
                        trailingIcon = {
                            if (search.isNotEmpty())
                                IconButton(onClick = { search = "" }) {
                                    Icon(Icons.Outlined.Close, "Clear search")
                                }
                        },
                        modifier = Modifier.fillMaxWidth(),
                        singleLine = true,
                    )
                else if (search.isNotEmpty())
                    AssistChip(
                        onClick = { showSearch = true },
                        label = { Text("Search: $search") },
                        leadingIcon = { Icon(Icons.Outlined.Search, null, Modifier.size(18.dp)) },
                    )
                if (category != "All")
                    InputChip(
                        true,
                        onClick = { filters = true },
                        label = { Text(category) },
                        trailingIcon = {
                            IconButton(onClick = { category = "All" }) {
                                Icon(Icons.Outlined.Close, "Clear category")
                            }
                        },
                    )
                Text(
                    "${apps.size} apps",
                    style = MaterialTheme.typography.labelMedium,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
                if (apps.isEmpty()) InfoCard("No apps match", "Try a different search, category or date.")
            }
        }
        items(apps, key = { "app:${it.app}" }, contentType = { "app" }) { app -> AppCard(app, model) }
    }
    if (filters) {
        var selection by remember { mutableStateOf(category) }
        var ordering by remember { mutableStateOf(sort) }
        var all by remember { mutableStateOf(installed) }
        AlertDialog(
            onDismissRequest = { filters = false },
            title = { Text("Filter apps") },
            text = {
                Column(
                    Modifier.heightIn(max = 420.dp).verticalScroll(rememberScrollState()),
                    verticalArrangement = Arrangement.spacedBy(12.dp),
                ) {
                    Text("Show", style = MaterialTheme.typography.titleSmall)
                    Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                        FilterChip(!all, onClick = { all = false }, label = { Text("Used today") })
                        FilterChip(all, onClick = { all = true }, label = { Text("Installed") })
                    }
                    Text("Sort by", style = MaterialTheme.typography.titleSmall)
                    listOf("time" to "Most time", "visits" to "Most visits", "name" to "Name")
                        .forEach { (key, label) ->
                            Row(verticalAlignment = Alignment.CenterVertically) {
                                RadioButton(ordering == key, onClick = { ordering = key })
                                Text(label)
                            }
                        }
                    Text("Category", style = MaterialTheme.typography.titleSmall)
                    (listOf("All") + Preferences.categories).chunked(2).forEach { row ->
                        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                            row.forEach { name ->
                                FilterChip(
                                    selection == name,
                                    onClick = { selection = name },
                                    label = { Text(name) },
                                    modifier = Modifier.weight(1f),
                                )
                            }
                        }
                    }
                }
            },
            confirmButton = {
                TextButton(
                    onClick = {
                        category = selection
                        sort = ordering
                        installed = all
                        filters = false
                    }
                ) {
                    Text("Apply")
                }
            },
            dismissButton = {
                TextButton(
                    onClick = {
                        selection = "All"
                        ordering = "time"
                        all = false
                    }
                ) {
                    Text("Reset")
                }
            },
        )
    }
}

@Composable
internal fun AppCard(app: AppUsage, model: AppModel) {
    var editing by remember { mutableStateOf(false) }
    var unlock by remember { mutableStateOf<(() -> Unit)?>(null) }
    val context = LocalContext.current
    OutlinedCard(onClick = { editing = true }, modifier = Modifier.fillMaxWidth()) {
        Column(
            Modifier.padding(if (LocalDisplay.current.compact) 12.dp else 16.dp),
            verticalArrangement = Arrangement.spacedBy(12.dp),
        ) {
            Row(
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.spacedBy(12.dp),
            ) {
                AppIcon(app.app, app.label)
                Column(Modifier.weight(1f)) {
                    Text(app.label, fontWeight = FontWeight.SemiBold, maxLines = 2)
                    Text(
                        "${app.category} · ${app.sessions} visits",
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                    )
                }
                Column(horizontalAlignment = Alignment.End) {
                    Text(usage(app.milliseconds), fontWeight = FontWeight.SemiBold)
                    if (model.preferences.blockRules.any { it.app == app.app && it.enabled })
                        Text(
                            "RULE ACTIVE",
                            style = MaterialTheme.typography.labelSmall,
                            color = MaterialTheme.colorScheme.primary,
                        )
                }
            }
            if (app.limit > 0) {
                LinearProgressIndicator(
                    progress = { (app.milliseconds / (app.limit * 60_000f)).coerceIn(0f, 1f) },
                    modifier = Modifier.fillMaxWidth(),
                    color =
                        if (app.milliseconds >= app.limit * 60_000L) MaterialTheme.colorScheme.error
                        else MaterialTheme.colorScheme.primary,
                )
                Text(
                    "${duration(app.milliseconds)} / ${app.limit}m daily goal",
                    style = MaterialTheme.typography.labelSmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }
        }
    }
    if (editing) {
        var category by remember { mutableStateOf(app.category) }
        var limit by remember { mutableStateOf(app.limit.toString()) }
        var exclude by remember { mutableStateOf(false) }
        val parsed = limit.toIntOrNull()
        AlertDialog(
            onDismissRequest = { editing = false },
            title = {
                Row(
                    verticalAlignment = Alignment.CenterVertically,
                    horizontalArrangement = Arrangement.spacedBy(12.dp),
                ) {
                    AppIcon(app.app, app.label)
                    Text(app.label)
                }
            },
            text = {
                Column(
                    Modifier.heightIn(max = 480.dp).verticalScroll(rememberScrollState()),
                    verticalArrangement = Arrangement.spacedBy(14.dp),
                ) {
                    Text(
                        app.app,
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                    )
                    Row(horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                        Metric("Time", usage(app.milliseconds), "Selected day", Modifier.weight(1f))
                        Metric(
                            "Visits",
                            app.sessions.toString(),
                            "Foreground sessions",
                            Modifier.weight(1f),
                        )
                    }
                    Text("Category", fontWeight = FontWeight.SemiBold)
                    Preferences.categories.chunked(2).forEach { row ->
                        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                            row.forEach { name ->
                                FilterChip(
                                    category == name,
                                    onClick = { category = name },
                                    label = { Text(name) },
                                    modifier = Modifier.weight(1f),
                                )
                            }
                        }
                    }
                    OutlinedTextField(
                        limit,
                        { limit = it.filter(Char::isDigit).take(4) },
                        label = { Text("Daily goal · minutes, 0 = off") },
                        isError = parsed == null || parsed !in 0..1440,
                        keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                        singleLine = true,
                    )
                    Text(
                        "This is a daily goal for comparison. Set a rolling 24-hour allowance or Strict rule separately in Block.",
                        style = MaterialTheme.typography.bodySmall,
                    )
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        Checkbox(exclude, { exclude = it })
                        Text(
                            "Exclude and delete saved app activity",
                            style = MaterialTheme.typography.bodyMedium,
                        )
                    }
                    OutlinedButton(
                        onClick = {
                            runCatching {
                                context.startActivity(
                                    Intent(
                                        Settings.ACTION_APPLICATION_DETAILS_SETTINGS,
                                        Uri.parse("package:${app.app}"),
                                    )
                                )
                            }
                        },
                        modifier = Modifier.fillMaxWidth(),
                    ) {
                        Text("Android app settings")
                    }
                }
            },
            confirmButton = {
                TextButton(
                    enabled = parsed != null && parsed in 0..1440,
                    onClick = {
                        val save: () -> Unit = {
                            model.edit { repo ->
                                repo.preferences.setCategory(app.app, category)
                                repo.preferences.setLimit(app.app, parsed ?: 0)
                                if (exclude) repo.exclude(app.app)
                            }
                            editing = false
                        }
                        val protected =
                            model.preferences.pinHash.isNotEmpty() &&
                                model.preferences.blockRules.any {
                                    it.app == app.app && it.enabled
                                } &&
                                (exclude || parsed != app.limit)
                        if (protected) unlock = save else save()
                    },
                ) {
                    Text("Save changes")
                }
            },
            dismissButton = { TextButton(onClick = { editing = false }) { Text("Cancel") } },
        )
    }
    if (unlock != null)
        PinChallenge(
            model.preferences,
            onVerified = {
                val action = unlock
                unlock = null
                action?.invoke()
            },
            onDismiss = { unlock = null },
        )
}
