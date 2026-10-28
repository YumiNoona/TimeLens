package `in`.venusapp.timelens.ui

import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp

@Composable
internal fun TimelineScreen(state: UiState, model: AppModel) {
    var search by rememberSaveable { mutableStateOf("") }
    var category by rememberSaveable { mutableStateOf("All") }
    var grouped by rememberSaveable { mutableStateOf(false) }
    var visible by rememberSaveable { mutableIntStateOf(150) }
    var expandedApps by rememberSaveable { mutableStateOf(emptyList<String>()) }
    val day = state.day ?: return
    val labels =
        (state.installed.associate { it.app to it.label } +
            day.apps.associate { it.app to it.label })
    val rows =
        day.sessions
            .filter {
                (labels[it.app] ?: it.app).contains(search, true) &&
                    (category == "All" || model.preferences.category(it.app) == category)
            }
            .asReversed()
    Column(verticalArrangement = Arrangement.spacedBy(14.dp)) {
        SectionTitle(
            "Every switch, in context.",
            "A chronological record of foreground apps. Gaps stay gaps.",
        )
        OutlinedTextField(
            search,
            { search = it },
            label = { Text("Search timeline") },
            modifier = Modifier.fillMaxWidth(),
            singleLine = true,
        )
        Row(
            Modifier.horizontalScroll(rememberScrollState()),
            horizontalArrangement = Arrangement.spacedBy(8.dp),
        ) {
            (listOf("All") + `in`.venusapp.timelens.data.Preferences.categories).forEach { name ->
                FilterChip(category == name, onClick = { category = name }, label = { Text(name) })
            }
        }
        SettingSwitch(
            "Group by app",
            "Switch between individual visits and app totals",
            grouped,
            { grouped = it },
        )
        if (rows.isEmpty()) EmptyActivity()
        if (grouped)
            rows
                .groupBy { it.app }
                .entries
                .sortedByDescending { it.value.sumOf { row -> row.milliseconds } }
                .forEach { (app, visits) ->
                    OutlinedCard(Modifier.fillMaxWidth()) {
                        Column(
                            Modifier.padding(16.dp),
                            verticalArrangement = Arrangement.spacedBy(10.dp),
                        ) {
                            Row(
                                verticalAlignment = Alignment.CenterVertically,
                                horizontalArrangement = Arrangement.spacedBy(12.dp),
                            ) {
                                AppIcon(app, labels[app] ?: app)
                                Column(Modifier.weight(1f)) {
                                    Text(labels[app] ?: app, fontWeight = FontWeight.SemiBold)
                                    Text(
                                        "${visits.size} visits · ${model.preferences.category(app)}",
                                        style = MaterialTheme.typography.bodySmall,
                                    )
                                }
                                Text(usage(visits.sumOf { it.milliseconds }))
                            }
                            visits.take(if (app in expandedApps) visits.size else 8).forEach { row ->
                                Text(
                                    "${timeLabel(row.start)} – ${timeLabel(row.end)} · ${usage(row.milliseconds)}",
                                    style = MaterialTheme.typography.bodySmall,
                                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                                )
                            }
                            if (visits.size > 8) TextButton(onClick = {
                                expandedApps = if (app in expandedApps) expandedApps - app else expandedApps + app
                            }) { Text(if (app in expandedApps) "Show fewer visits" else "Show all ${visits.size} visits") }
                        }
                    }
                }
        else
            rows.take(visible).forEach { row ->
                OutlinedCard(Modifier.fillMaxWidth()) {
                    Row(
                        Modifier.padding(16.dp),
                        verticalAlignment = Alignment.CenterVertically,
                        horizontalArrangement = Arrangement.spacedBy(12.dp),
                    ) {
                        AppIcon(row.app, labels[row.app] ?: row.app, Modifier.size(38.dp))
                        Column(Modifier.weight(1f)) {
                            Text(labels[row.app] ?: row.app, fontWeight = FontWeight.Medium)
                            Text(
                                "${timeLabel(row.start)} – ${timeLabel(row.end)}",
                                style = MaterialTheme.typography.bodySmall,
                                color = MaterialTheme.colorScheme.onSurfaceVariant,
                            )
                        }
                        Text(usage(row.milliseconds), fontWeight = FontWeight.SemiBold)
                    }
                }
            }
        if (!grouped && rows.size > visible)
            OutlinedButton(onClick = { visible += 150 }, modifier = Modifier.fillMaxWidth()) {
                Text("Show more visits")
            }
    }
}
