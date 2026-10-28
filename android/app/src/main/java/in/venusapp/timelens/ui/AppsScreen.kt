package `in`.venusapp.timelens.ui

import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.horizontalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import `in`.venusapp.timelens.data.*

@Composable
internal fun AppsFilter(day: DayUsage?, model: AppModel) {
    var search by rememberSaveable { mutableStateOf("") }
    var category by rememberSaveable { mutableStateOf("All") }
    Column(verticalArrangement = Arrangement.spacedBy(12.dp)) {
        OutlinedTextField(search, onValueChange = { search = it }, label = { Text("Search apps") }, leadingIcon = { Icon(Icons.Outlined.Search, null) }, modifier = Modifier.fillMaxWidth(), singleLine = true)
        Row(Modifier.horizontalScroll(rememberScrollState()), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            (listOf("All") + Preferences.categories).forEach { item -> FilterChip(selected = category == item, onClick = { category = item }, label = { Text(item) }) }
        }
        val apps = day?.apps.orEmpty().filter { (category == "All" || it.category == category) && (it.label.contains(search, true) || it.app.contains(search, true)) }
        if (apps.isEmpty()) InfoCard("No apps to show", "Try another search or day. Grant usage access to import activity.")
        apps.forEach { app -> key(app.app) { AppCard(app, model) } }
    }
}

@Composable
internal fun AppCard(app: AppUsage, model: AppModel) {
    var editing by remember { mutableStateOf(false) }
    OutlinedCard(onClick = { editing = true }, modifier = Modifier.fillMaxWidth()) {
        Row(Modifier.padding(16.dp), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(12.dp)) {
            Surface(shape = MaterialTheme.shapes.medium, color = MaterialTheme.colorScheme.secondaryContainer) { Text(app.label.take(1).uppercase(), modifier = Modifier.padding(14.dp), fontWeight = FontWeight.Bold) }
            Column(Modifier.weight(1f)) { Text(app.label, fontWeight = FontWeight.SemiBold); Text("${app.category} · ${app.sessions} sessions", style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
                if (app.limit > 0) Text(if (app.milliseconds >= app.limit * 60_000L) "Budget reached · ${app.limit}m" else "Daily budget · ${app.limit}m", style = MaterialTheme.typography.labelSmall, color = MaterialTheme.colorScheme.primary)
            }
            Text(duration(app.milliseconds), fontWeight = FontWeight.SemiBold)
        }
    }
    if (editing) {
        var category by remember { mutableStateOf(app.category) }
        var limit by remember { mutableStateOf(app.limit.toString()) }
        var exclude by remember { mutableStateOf(false) }
        val parsedLimit = limit.toIntOrNull()
        AlertDialog(onDismissRequest = { editing = false }, title = { Text(app.label) }, text = { Column(verticalArrangement = Arrangement.spacedBy(12.dp)) {
            Text(app.app, style = MaterialTheme.typography.bodySmall)
            Row(Modifier.horizontalScroll(rememberScrollState()), horizontalArrangement = Arrangement.spacedBy(6.dp)) { Preferences.categories.forEach { value -> FilterChip(selected = value == category, onClick = { category = value }, label = { Text(value) }) } }
            OutlinedTextField(limit, onValueChange = { limit = it.filter(Char::isDigit).take(4) }, label = { Text("Daily budget in minutes (0 = off)") }, isError = parsedLimit == null || parsedLimit !in 0..1440, singleLine = true)
            Row(verticalAlignment = Alignment.CenterVertically) { Checkbox(exclude, onCheckedChange = { exclude = it }); Text("Exclude this app and delete its saved activity") }
            Text("Budgets are reminders shown here. TimeLens does not block other Android apps.", style = MaterialTheme.typography.bodySmall)
        } }, confirmButton = { TextButton(enabled = parsedLimit != null && parsedLimit in 0..1440, onClick = {
            model.edit { repo -> repo.preferences.setCategory(app.app, category); repo.preferences.setLimit(app.app, parsedLimit ?: 0); if (exclude) repo.exclude(app.app) }
            editing = false
        }) { Text("Save") } }, dismissButton = { TextButton(onClick = { editing = false }) { Text("Cancel") } })
    }
}
