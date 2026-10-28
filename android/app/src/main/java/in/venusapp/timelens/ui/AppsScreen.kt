package `in`.venusapp.timelens.ui

import android.content.Intent
import android.net.Uri
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
import `in`.venusapp.timelens.data.*

@Composable
internal fun AppsScreen(state: UiState, model: AppModel) {
    var visible by rememberSaveable { mutableIntStateOf(60) }
    var search by rememberSaveable { mutableStateOf("") }
    var category by rememberSaveable { mutableStateOf("All") }
    var installed by rememberSaveable { mutableStateOf(false) }
    var sort by rememberSaveable { mutableStateOf("time") }
    val usage = state.day?.apps.orEmpty().associateBy { it.app }
    val source =
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
    val apps =
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
    Column(verticalArrangement = Arrangement.spacedBy(14.dp)) {
        SectionTitle(
            "Your apps, clearly.",
            "Real app icons, time, visits, categories and daily budgets.",
        )
        OutlinedTextField(
            search,
            { search = it },
            label = { Text("Search apps") },
            leadingIcon = { Icon(Icons.Outlined.Search, null) },
            modifier = Modifier.fillMaxWidth(),
            singleLine = true,
        )
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            FilterChip(!installed, onClick = { installed = false }, label = { Text("Used today") })
            FilterChip(
                installed,
                onClick = { installed = true },
                label = { Text("Installed apps") },
            )
        }
        Row(
            Modifier.horizontalScroll(rememberScrollState()),
            horizontalArrangement = Arrangement.spacedBy(8.dp),
        ) {
            (listOf("All") + Preferences.categories).forEach { name ->
                FilterChip(category == name, onClick = { category = name }, label = { Text(name) })
            }
        }
        Row(
            Modifier.horizontalScroll(rememberScrollState()),
            horizontalArrangement = Arrangement.spacedBy(8.dp),
        ) {
            listOf("time" to "Most time", "visits" to "Most visits", "name" to "A–Z").forEach {
                (key, label) ->
                FilterChip(sort == key, onClick = { sort = key }, label = { Text(label) })
            }
        }
        Text(
            "${apps.size} apps",
            style = MaterialTheme.typography.labelMedium,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
        )
        if (apps.isEmpty()) InfoCard("No apps match", "Try a different search, category or date.")
        apps.take(visible).forEach { app -> key(app.app) { AppCard(app, model) } }
        if (apps.size > visible)
            OutlinedButton(onClick = { visible += 60 }, modifier = Modifier.fillMaxWidth()) {
                Text("Show more apps")
            }
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
                    "${duration(app.milliseconds)} / ${app.limit}m daily budget",
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
                        label = { Text("Daily budget · minutes, 0 = off") },
                        isError = parsed == null || parsed !in 0..1440,
                        keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                        singleLine = true,
                    )
                    Text(
                        "To enforce this budget, add an After a daily limit rule in Focus → Blocking.",
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
