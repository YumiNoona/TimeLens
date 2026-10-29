package `in`.venusapp.timelens.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import java.time.LocalDate
import java.time.ZoneId

@Composable
internal fun HistoryScreen(
    state: UiState,
    model: AppModel,
    modifier: Modifier = Modifier,
    onDate: (LocalDate) -> Unit = { model.refresh(it) },
    onAccess: () -> Unit = {},
) {
    val range = state.historyRange
    val end = if (range == 365) LocalDate.now() else state.date
    val report = state.report?.takeIf { it.end == end && it.days == range }
    val series = report?.daily.orEmpty()
    val apps = report?.apps.orEmpty()
    val calendar = remember(series) { series.chunked(14) }
    val maximum = remember(series) { maxOf(1L, series.maxOfOrNull { it.second } ?: 1) }
    val timelineOptions = rememberTimelineOptions()
    val timeline = rememberTimelineContent(state, timelineOptions)
    var periodAppsExpanded by rememberSaveable { mutableStateOf(false) }
    var visibleApps by rememberSaveable { mutableIntStateOf(8) }
    val primary = MaterialTheme.colorScheme.primary
    val background = MaterialTheme.colorScheme.surfaceContainerHighest
    val web =
        remember(state.webHistory, state.date) {
            val from = state.date.atStartOfDay(ZoneId.systemDefault()).toInstant().toEpochMilli()
            val until =
                state.date
                    .plusDays(1)
                    .atStartOfDay(ZoneId.systemDefault())
                    .toInstant()
                    .toEpochMilli()
            state.webHistory
                .filter { it.end > from && it.start < until }
                .groupBy { it.app }
                .map { entry ->
                    entry.key to
                        entry.value.sumOf {
                            (minOf(it.end, until) - maxOf(it.start, from)).coerceAtLeast(0)
                        }
                }
                .sortedByDescending { it.second }
        }
    LazyColumn(
        modifier,
        contentPadding = PaddingValues(if (LocalDisplay.current.compact) 14.dp else 20.dp),
        verticalArrangement = Arrangement.spacedBy(18.dp),
    ) {
        item(key = "history-date") { DateSelector(state.date, onDate) }
        item(key = "history-loading") {
            Box(Modifier.fillMaxWidth().height(4.dp)) {
                if (state.reportLoading) LinearProgressIndicator(Modifier.fillMaxWidth())
            }
        }
        if (state.day?.hasAccess == false) item(key = "history-access") { PermissionCard(onAccess) }
        state.error?.let { message ->
            item(key = "history-error") { InfoCard("Could not refresh", message) }
        }
        item(key = "history-controls") {
            Column(verticalArrangement = Arrangement.spacedBy(14.dp)) {
                SectionTitle("Your history.", "Trends across days, plus every visit in order.")
                if (range == 365)
                    Text(
                        "Last year · choose any saved day to inspect its visits below.",
                        style = MaterialTheme.typography.bodySmall,
                    )
                HistoryRangeSelector(range, model::setHistoryRange)
                Row(
                    Modifier.height(IntrinsicSize.Min),
                    horizontalArrangement = Arrangement.spacedBy(12.dp),
                ) {
                    Metric(
                        "Total time",
                        report?.let { usage(it.total) } ?: "…",
                        "${apps.size} apps",
                        Modifier.weight(1f),
                    )
                    Metric(
                        "Daily average",
                        report?.let { usage(it.total / range) } ?: "…",
                        "Saved activity",
                        Modifier.weight(1f),
                    )
                }
                if (model.preferences.retention < range) {
                    InfoCard(
                        "Shorter retention is enabled",
                        "Only the last ${model.preferences.retention} days are retained. Deleted history cannot be recovered.",
                    )
                    TextButton(onClick = { model.edit { it.preferences.retention = 365 } }) {
                        Text("Keep one year of history")
                    }
                }
            }
        }
        item(key = "week-chart") { WeekChart(state, onDate) }
        item(key = "category-chart") { CategoryChart(apps) }
        if (apps.isNotEmpty())
            item(key = "period-app-title") {
                OutlinedCard(
                    onClick = { periodAppsExpanded = !periodAppsExpanded },
                    modifier = Modifier.fillMaxWidth(),
                ) {
                    Row(
                        Modifier.padding(18.dp),
                        verticalAlignment = Alignment.CenterVertically,
                        horizontalArrangement = Arrangement.spacedBy(12.dp),
                    ) {
                        Icon(Icons.Outlined.Apps, null)
                        Column(Modifier.weight(1f)) {
                            Text("Period app totals", style = MaterialTheme.typography.titleMedium)
                            Text(
                                "$range days · " + apps.size + " apps",
                                style = MaterialTheme.typography.bodySmall,
                            )
                        }
                        Icon(
                            if (periodAppsExpanded) Icons.Outlined.ExpandLess
                            else Icons.Outlined.ExpandMore,
                            if (periodAppsExpanded) "Collapse period app totals"
                            else "Expand period app totals",
                        )
                    }
                }
            }
        if (periodAppsExpanded) {
            items(
                apps.take(visibleApps),
                key = { "period:${it.app}" },
                contentType = { "period-app" },
            ) { app ->
                OutlinedCard(Modifier.fillMaxWidth()) {
                    Row(
                        Modifier.padding(14.dp),
                        verticalAlignment = Alignment.CenterVertically,
                        horizontalArrangement = Arrangement.spacedBy(12.dp),
                    ) {
                        AppIcon(app.app, app.label)
                        Column(Modifier.weight(1f)) {
                            Text(app.label, fontWeight = FontWeight.Medium)
                            Text(
                                "${app.sessions} visits · ${app.category}",
                                style = MaterialTheme.typography.bodySmall,
                            )
                        }
                        Text(usage(app.milliseconds))
                    }
                }
            }
            if (apps.size > visibleApps)
                item(key = "more-period-apps") {
                    TextButton(onClick = { visibleApps += 20 }) {
                        Text("Show more apps in this period")
                    }
                }
        }
        item(key = "calendar-title") {
            Text("Activity calendar", style = MaterialTheme.typography.titleMedium)
        }
        items(calendar.size, key = { "calendar-$it" }, contentType = { "calendar-row" }) { index ->
            val chunk = calendar[index]
            Row(horizontalArrangement = Arrangement.spacedBy(5.dp)) {
                chunk.forEach { (date, ms) ->
                    Box(
                        Modifier.weight(1f)
                            .aspectRatio(1f)
                            .clip(RoundedCornerShape(4.dp))
                            .background(
                                if (ms == 0L) background
                                else primary.copy(alpha = .25f + .75f * ms / maximum)
                            )
                            .clickable { onDate(date) }
                            .semantics { contentDescription = "$date: ${duration(ms)}" }
                    )
                }
                repeat(14 - chunk.size) { Spacer(Modifier.weight(1f)) }
            }
        }
        item(key = "calendar-caption") {
            Text(
                "Empty cells mean no saved activity; Android may no longer have older events.",
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
        }
        timelineItems(timeline, timelineOptions)
        item(key = "website-title") {
            SectionTitle(
                "Website history",
                "Observed domains on the selected day · included in browser app time",
            )
        }
        if (web.isEmpty())
            item(key = "website-empty") {
                InfoCard(
                    "No website observations",
                    "Enable optional website tracking from Block → Websites. Hidden browser bars and unsupported browsers leave gaps.",
                )
            }
        items(web, key = { "website:${it.first}" }, contentType = { "website-row" }) { (host, ms) ->
            OutlinedCard(Modifier.fillMaxWidth()) {
                Row(
                    Modifier.padding(18.dp),
                    verticalAlignment = Alignment.CenterVertically,
                    horizontalArrangement = Arrangement.spacedBy(14.dp),
                ) {
                    Icon(Icons.Outlined.Language, null)
                    Text(host, Modifier.weight(1f), fontWeight = FontWeight.Medium)
                    Text(usage(ms))
                }
            }
        }
    }
}
