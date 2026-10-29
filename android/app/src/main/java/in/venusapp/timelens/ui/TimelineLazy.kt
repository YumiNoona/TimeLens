package `in`.venusapp.timelens.ui

import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyListScope
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.runtime.saveable.listSaver
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.semantics.stateDescription
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import `in`.venusapp.timelens.data.Preferences
import `in`.venusapp.timelens.domain.Session

@Stable
internal class TimelineOptions {
    var search by mutableStateOf("")
    var category by mutableStateOf("All")
    var sort by mutableStateOf("time")
    var expanded by mutableStateOf(emptyList<String>())

    companion object {
        val Saver =
            listSaver<TimelineOptions, Any>(
                save = {
                    listOf(
                        "accordion-v1",
                        it.search,
                        it.category,
                        it.sort,
                        it.expanded.joinToString("|"),
                    )
                },
                restore = { saved ->
                    TimelineOptions().apply {
                        if (saved.getOrNull(0) == "accordion-v1" && saved.getOrNull(2) is String) {
                            search = saved[1] as String
                            category = saved[2] as String
                            sort = saved[3] as String
                        } else {
                            search = saved.getOrNull(0) as? String ?: ""
                            category = saved.getOrNull(1) as? String ?: "All"
                        }
                        expanded =
                            (saved.getOrNull(4) as? String)
                                .orEmpty()
                                .split('|')
                                .filter(String::isNotEmpty)
                    }
                },
            )
    }
}

internal data class HistoryAppGroup(
    val app: String,
    val label: String,
    val category: String,
    val visits: List<Session>,
    val total: Long,
)

internal data class TimelineContent(val groups: List<HistoryAppGroup>)

@Composable
internal fun rememberTimelineOptions() =
    rememberSaveable(saver = TimelineOptions.Saver) { TimelineOptions() }

@Composable
internal fun rememberTimelineContent(state: UiState, options: TimelineOptions): TimelineContent {
    val labels =
        remember(state.installed, state.day?.apps) {
            state.installed.associate { it.app to it.label } +
                state.day?.apps.orEmpty().associate { it.app to it.label }
        }
    val categories =
        remember(state.day?.apps) { state.day?.apps.orEmpty().associate { it.app to it.category } }
    val groups =
        remember(state.day?.sessions, labels, categories) {
            state.day
                ?.sessions
                .orEmpty()
                .asReversed()
                .groupBy { it.app }
                .map { (app, visits) ->
                    HistoryAppGroup(
                        app,
                        labels[app] ?: app,
                        categories[app] ?: "Other",
                        visits,
                        visits.sumOf { it.milliseconds },
                    )
                }
        }
    val search = options.search
    val category = options.category
    val sort = options.sort
    return remember(groups, search, category, sort) {
        val filtered = groups.filter {
            (it.label.contains(search, true) || it.app.contains(search, true)) &&
                (category == "All" || it.category == category)
        }
        TimelineContent(
            when (sort) {
                "visits" -> filtered.sortedByDescending { it.visits.size }
                "name" -> filtered.sortedBy { it.label.lowercase() }
                else -> filtered.sortedByDescending { it.total }
            }
        )
    }
}

/** One app header per selected day. Only expanded visits become lazy-list entries. */
internal fun LazyListScope.timelineItems(content: TimelineContent, options: TimelineOptions) {
    item(key = "timeline-controls", contentType = "timeline-controls") {
        var showSearch by rememberSaveable { mutableStateOf(false) }
        var filters by remember { mutableStateOf(false) }
        Column(verticalArrangement = Arrangement.spacedBy(12.dp)) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Column(Modifier.weight(1f)) {
                    Text("App history", style = MaterialTheme.typography.titleLarge)
                    Text(
                        "Selected day · ${content.groups.size} apps",
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                    )
                }
                if (options.expanded.isNotEmpty())
                    IconButton(onClick = { options.expanded = emptyList() }) {
                        Icon(Icons.Outlined.UnfoldLess, "Collapse all apps")
                    }
                IconButton(onClick = { showSearch = !showSearch }) {
                    Icon(
                        if (showSearch) Icons.Outlined.SearchOff else Icons.Outlined.Search,
                        if (showSearch) "Hide history search" else "Search app history",
                    )
                }
                IconButton(onClick = { filters = true }) {
                    Icon(Icons.Outlined.Tune, "Filter and sort app history")
                }
            }
            if (showSearch)
                OutlinedTextField(
                    options.search,
                    { options.search = it },
                    label = { Text("Search app history") },
                    modifier = Modifier.fillMaxWidth(),
                    singleLine = true,
                    trailingIcon = {
                        if (options.search.isNotEmpty())
                            IconButton(onClick = { options.search = "" }) {
                                Icon(Icons.Outlined.Close, "Clear history search")
                            }
                    },
                )
            else if (options.search.isNotEmpty())
                AssistChip(
                    onClick = { showSearch = true },
                    label = { Text("Search: ${options.search}") },
                    leadingIcon = { Icon(Icons.Outlined.Search, null, Modifier.size(16.dp)) },
                )
            if (options.category != "All")
                InputChip(
                    true,
                    onClick = { filters = true },
                    label = { Text(options.category) },
                    trailingIcon = {
                        IconButton(onClick = { options.category = "All" }) {
                            Icon(Icons.Outlined.Close, "Clear history category")
                        }
                    },
                )
            if (content.groups.isEmpty())
                InfoCard("No apps match", "Try another date, search or category.")
        }
        if (filters) {
            var category by remember { mutableStateOf(options.category) }
            var sort by remember { mutableStateOf(options.sort) }
            AlertDialog(
                onDismissRequest = { filters = false },
                title = { Text("History filters") },
                text = {
                    Column(
                        Modifier.heightIn(max = 430.dp).verticalScroll(rememberScrollState()),
                        verticalArrangement = Arrangement.spacedBy(10.dp),
                    ) {
                        Text("Sort apps", style = MaterialTheme.typography.titleSmall)
                        listOf("time" to "Most time", "visits" to "Most visits", "name" to "Name")
                            .forEach { (key, label) ->
                                Row(
                                    Modifier.fillMaxWidth().clickable { sort = key },
                                    verticalAlignment = Alignment.CenterVertically,
                                ) {
                                    RadioButton(sort == key, onClick = { sort = key })
                                    Text(label)
                                }
                            }
                        Text("Category", style = MaterialTheme.typography.titleSmall)
                        (listOf("All") + Preferences.categories).chunked(2).forEach { row ->
                            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                                row.forEach { name ->
                                    FilterChip(
                                        category == name,
                                        onClick = { category = name },
                                        label = {
                                            Text(name, style = MaterialTheme.typography.labelMedium)
                                        },
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
                            options.category = category
                            options.sort = sort
                            filters = false
                        }
                    ) {
                        Text("Apply")
                    }
                },
                dismissButton = {
                    TextButton(
                        onClick = {
                            category = "All"
                            sort = "time"
                        }
                    ) {
                        Text("Reset")
                    }
                },
            )
        }
    }
    content.groups.forEach { group ->
        val expanded = group.app in options.expanded
        item(key = "group-${group.app}", contentType = "group-header") {
            OutlinedCard(
                onClick = {
                    options.expanded =
                        if (expanded) options.expanded - group.app else options.expanded + group.app
                },
                modifier =
                    Modifier.fillMaxWidth().testTag("history-app-${group.app}").semantics {
                        stateDescription = if (expanded) "Expanded" else "Collapsed"
                    },
            ) {
                Row(
                    Modifier.padding(16.dp),
                    verticalAlignment = Alignment.CenterVertically,
                    horizontalArrangement = Arrangement.spacedBy(12.dp),
                ) {
                    AppIcon(group.app, group.label, Modifier.size(40.dp))
                    Column(Modifier.weight(1f)) {
                        Text(group.label, fontWeight = FontWeight.SemiBold, maxLines = 2)
                        Text(
                            "${group.visits.size} ${if(group.visits.size==1) "visit" else "visits"} · ${group.category}",
                            style = MaterialTheme.typography.bodySmall,
                            color = MaterialTheme.colorScheme.onSurfaceVariant,
                        )
                    }
                    Text(usage(group.total), fontWeight = FontWeight.SemiBold)
                    Icon(
                        if (expanded) Icons.Outlined.ExpandLess else Icons.Outlined.ExpandMore,
                        null,
                        Modifier.size(20.dp),
                    )
                }
            }
        }
        if (expanded)
            items(
                group.visits,
                key = { "group-visit:${it.app}:${it.start}" },
                contentType = { "group-visit" },
            ) { row ->
                Surface(
                    modifier =
                        Modifier.fillMaxWidth()
                            .padding(start = 22.dp)
                            .testTag("history-visit-${row.app}-${row.start}"),
                    shape = MaterialTheme.shapes.medium,
                    color = MaterialTheme.colorScheme.surfaceContainerLow,
                ) {
                    Row(
                        Modifier.padding(horizontal = 14.dp, vertical = 10.dp),
                        verticalAlignment = Alignment.CenterVertically,
                        horizontalArrangement = Arrangement.spacedBy(12.dp),
                    ) {
                        Icon(
                            Icons.Outlined.Schedule,
                            null,
                            Modifier.size(16.dp),
                            tint = MaterialTheme.colorScheme.onSurfaceVariant,
                        )
                        Text(
                            "${timeLabel(row.start)} – ${timeLabel(row.end)}",
                            Modifier.weight(1f),
                            style = MaterialTheme.typography.bodySmall,
                        )
                        Text(usage(row.milliseconds), style = MaterialTheme.typography.labelMedium)
                    }
                }
            }
    }
}
