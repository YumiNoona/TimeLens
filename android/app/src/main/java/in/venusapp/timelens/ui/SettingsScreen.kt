package `in`.venusapp.timelens.ui

import androidx.compose.foundation.layout.*
import androidx.compose.material.icons.outlined.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import `in`.venusapp.timelens.data.*

@Composable
internal fun SettingsScreen(model: AppModel, theme: String, onTheme: (String) -> Unit, onAccess: () -> Unit, onExport: (Boolean) -> Unit, exportEnabled: Boolean) {
    val prefs = model.preferences
    var budget by remember { mutableStateOf(prefs.budget.toString()) }
    var confirmDelete by remember { mutableStateOf(false) }
    val state by model.state.collectAsStateWithLifecycle()
    Column(verticalArrangement = Arrangement.spacedBy(20.dp)) {
        SectionTitle("Your app. Your rules.", "Local storage. No account. No analytics or cloud service.")
        OutlinedButton(onClick = onAccess, modifier = Modifier.fillMaxWidth()) { Text("Manage Android usage access") }
        Text("Appearance", style = MaterialTheme.typography.titleMedium)
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) { listOf("system", "dark", "light").forEach { value -> FilterChip(selected = theme == value, onClick = { onTheme(value) }, label = { Text(value.replaceFirstChar(Char::uppercase)) }) } }
        Text("Daily screen budget", style = MaterialTheme.typography.titleMedium)
        OutlinedTextField(budget, onValueChange = { budget = it.filter(Char::isDigit).take(4) }, label = { Text("Minutes · 15 to 1440") }, singleLine = true, modifier = Modifier.fillMaxWidth())
        Button(enabled = budget.toIntOrNull()?.let { it in 15..1440 } == true, onClick = { model.edit { it.preferences.budget = budget.toInt() } }) { Text("Save budget") }
        Text("Keep local history", style = MaterialTheme.typography.titleMedium)
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) { listOf(7, 30, 90, 365).forEach { value -> FilterChip(selected = prefs.retention == value, onClick = { model.edit { it.preferences.retention = value } }, label = { Text("${value}d") }) } }
        InfoCard("How history works", "TimeLens imports recent foreground events when you open or refresh the app. Android retains only a short event window; older history is available only when previously archived here. No background polling service runs. Check in regularly to build history.")
        if (prefs.excluded.isNotEmpty()) {
            Text("Excluded apps", style = MaterialTheme.typography.titleMedium)
            prefs.excluded.sorted().forEach { app -> OutlinedButton(onClick = { model.edit { it.preferences.restore(app) } }, modifier = Modifier.fillMaxWidth()) { Text("Restore tracking: $app") } }
        }
        Text("Export ${state.date}", style = MaterialTheme.typography.titleMedium)
        Text("Exports the day selected in Today, Apps or Timeline. Activity can be sensitive; choose where to save it.", style = MaterialTheme.typography.bodySmall)
        Row(horizontalArrangement = Arrangement.spacedBy(12.dp)) { OutlinedButton(enabled = exportEnabled, onClick = { onExport(true) }) { Text("Save JSON") }; OutlinedButton(enabled = exportEnabled, onClick = { onExport(false) }) { Text("Save CSV") } }
        InfoCard("Separate devices, optional connection", "This app tracks this phone independently from TimeLens for Windows. Encrypted local PC pairing and a combined device view are planned. No network connection or PC setup is required for phone tracking.")
        InfoCard("Android preview", "Foreground apps, history, categories, budgets, timeline and export are available. Website URLs, media playback detail, global input counts and strict blocking are not implemented in this preview.")
        OutlinedButton(onClick = { confirmDelete = true }, modifier = Modifier.fillMaxWidth()) { Text("Delete all saved phone activity", color = MaterialTheme.colorScheme.error) }
    }
    if (confirmDelete) AlertDialog(onDismissRequest = { confirmDelete = false }, title = { Text("Delete saved phone activity?") }, text = { Text("This removes TimeLens activity and resets its import boundary to now. Deleted history will not return on refresh. Android's system usage history and files you exported remain under your control.") }, confirmButton = { TextButton(onClick = { model.edit { it.clear() }; confirmDelete = false }) { Text("Delete activity") } }, dismissButton = { TextButton(onClick = { confirmDelete = false }) { Text("Cancel") } })
}
