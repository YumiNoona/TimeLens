package `in`.venusapp.timelens.ui

import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyListScope
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.rememberScrollState
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.runtime.saveable.listSaver
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import `in`.venusapp.timelens.data.Preferences
import `in`.venusapp.timelens.domain.Session

@Stable
internal class TimelineOptions {
    var search by mutableStateOf("")
    var category by mutableStateOf("All")
    var grouped by mutableStateOf(false)
    var visible by mutableIntStateOf(150)
    var expanded by mutableStateOf(emptyList<String>())

    companion object {
        val Saver =
            listSaver<TimelineOptions, Any>(
                save = {
                    listOf(
                        it.search,
                        it.category,
                        it.grouped,
                        it.visible,
                        it.expanded.joinToString("|"),
                    )
                },
                restore = {
                    TimelineOptions().apply {
                        search = it[0] as String
                        category = it[1] as String
                        grouped = it[2] as Boolean
                        visible = it[3] as Int
                        expanded = (it[4] as String).split('|').filter(String::isNotEmpty)
                    }
                },
            )
    }
}

internal data class TimelineContent(
    val labels: Map<String, String>,
    val rows: List<Session>,
    val groups: List<Pair<String, List<Session>>>,
)

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
    val search = options.search
    val category = options.category
    return remember(state.day?.sessions, labels, categories, search, category) {
        val rows =
            state.day?.sessions.orEmpty().asReversed().filter {
                (labels[it.app] ?: it.app).contains(search, true) &&
                    (category == "All" || categories[it.app] == category)
            }
        TimelineContent(
            labels,
            rows,
            rows
                .groupBy { it.app }
                .entries
                .sortedByDescending { it.value.sumOf { row -> row.milliseconds } }
                .map { it.key to it.value },
        )
    }
}

/** Entries belong directly to History's lazy list, including expanded grouped visits. */
internal fun LazyListScope.timelineItems(content: TimelineContent, options: TimelineOptions) {
    item(key = "timeline-controls", contentType = "timeline-controls") {
        Column(verticalArrangement = Arrangement.spacedBy(14.dp)) {
            SectionTitle(
                "Every switch, in context.",
                "Selected-day foreground visits. Gaps stay gaps.",
            )
            OutlinedTextField(
                options.search,
                { options.search = it },
                label = { Text("Search timeline") },
                modifier = Modifier.fillMaxWidth(),
                singleLine = true,
            )
            Row(
                Modifier.horizontalScroll(rememberScrollState()),
                horizontalArrangement = Arrangement.spacedBy(8.dp),
            ) {
                (listOf("All") + Preferences.categories).forEach { name ->
                    FilterChip(
                        options.category == name,
                        onClick = { options.category = name },
                        label = { Text(name) },
                    )
                }
            }
            SettingSwitch(
                "Group by app",
                "Individual visits or app totals",
                options.grouped,
                { options.grouped = it },
            )
            if (content.rows.isEmpty()) EmptyActivity()
        }
    }
    if (options.grouped)
        content.groups.forEach { (app, visits) ->
            item(key = "group-$app", contentType = "group-header") {
                OutlinedCard(Modifier.fillMaxWidth()) {
                    Row(
                        Modifier.padding(16.dp),
                        verticalAlignment = Alignment.CenterVertically,
                        horizontalArrangement = Arrangement.spacedBy(12.dp),
                    ) {
                        AppIcon(app, content.labels[app] ?: app)
                        Column(Modifier.weight(1f)) {
                            Text(content.labels[app] ?: app, fontWeight = FontWeight.SemiBold)
                            Text(
                                "${visits.size} visits",
                                style = MaterialTheme.typography.bodySmall,
                            )
                        }
                        Text(usage(visits.sumOf { it.milliseconds }))
                    }
                }
            }
            items(
                visits.take(if (app in options.expanded) visits.size else 8),
                key = { "group-visit:${it.app}:${it.start}" },
                contentType = { "group-visit" },
            ) { row ->
                Text(
                    "${timeLabel(row.start)} – ${timeLabel(row.end)} · ${usage(row.milliseconds)}",
                    style = MaterialTheme.typography.bodySmall,
                    modifier = Modifier.padding(horizontal = 18.dp),
                )
            }
            if (visits.size > 8)
                item(key = "expand-$app") {
                    TextButton(
                        onClick = {
                            options.expanded =
                                if (app in options.expanded) options.expanded - app
                                else options.expanded + app
                        }
                    ) {
                        Text(
                            if (app in options.expanded) "Show fewer visits"
                            else "Show all ${visits.size} visits"
                        )
                    }
                }
        }
    else {
        items(
            content.rows.take(options.visible),
            key = { "visit:${it.app}:${it.start}" },
            contentType = { "visit" },
        ) { row ->
            OutlinedCard(Modifier.fillMaxWidth()) {
                Row(
                    Modifier.padding(16.dp),
                    verticalAlignment = Alignment.CenterVertically,
                    horizontalArrangement = Arrangement.spacedBy(12.dp),
                ) {
                    AppIcon(row.app, content.labels[row.app] ?: row.app, Modifier.size(38.dp))
                    Column(Modifier.weight(1f)) {
                        Text(content.labels[row.app] ?: row.app, fontWeight = FontWeight.Medium)
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
        if (content.rows.size > options.visible)
            item(key = "more-visits") {
                OutlinedButton(
                    onClick = { options.visible += 150 },
                    modifier = Modifier.fillMaxWidth(),
                ) {
                    Text("Show more visits")
                }
            }
    }
}
