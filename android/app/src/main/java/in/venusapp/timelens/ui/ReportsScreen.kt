package `in`.venusapp.timelens.ui

import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import `in`.venusapp.timelens.data.*
import `in`.venusapp.timelens.domain.*
import java.time.ZoneId
import java.time.format.DateTimeFormatter

@Composable
internal fun HourlyChart(day: DayUsage) {
    val values =
        remember(day) { UsageAnalytics.hourly(day.sessions, day.date, ZoneId.systemDefault()) }
    val primary = MaterialTheme.colorScheme.primary
    val muted = MaterialTheme.colorScheme.surfaceContainerHighest
    OutlinedCard(Modifier.fillMaxWidth()) {
        Column(Modifier.padding(20.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
            Text("Your day, by the hour", fontWeight = FontWeight.SemiBold)
            Text(
                "Foreground activity · local time",
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
            Canvas(
                Modifier.fillMaxWidth().height(100.dp).semantics {
                    contentDescription =
                        values
                            .mapIndexed { hour, ms -> "$hour: ${duration(ms)}" }
                            .joinToString(", ")
                }
            ) {
                val max = maxOf(values.maxOrNull() ?: 0, 60_000L).toFloat()
                val width = size.width / 24
                values.forEachIndexed { index, ms ->
                    val height =
                        if (ms == 0L) 4.dp.toPx()
                        else (ms / max * (size.height - 4.dp.toPx())).coerceAtLeast(5.dp.toPx())
                    drawRoundRect(
                        if (ms > 0) primary else muted,
                        Offset(index * width, size.height - height),
                        Size((width - 3.dp.toPx()).coerceAtLeast(1f), height),
                        androidx.compose.ui.geometry.CornerRadius(3.dp.toPx()),
                    )
                }
            }
            Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween) {
                listOf("00", "06", "12", "18", "23").forEach {
                    Text(
                        it,
                        style = MaterialTheme.typography.labelSmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                    )
                }
            }
        }
    }
}

@Composable
internal fun CategoryChart(apps: List<AppUsage>) {
    val categories =
        remember(apps) {
            apps
                .groupBy { it.category }
                .map { (name, rows) -> name to rows.sumOf { it.milliseconds } }
                .sortedByDescending { it.second }
        }
    val total = maxOf(1, categories.sumOf { it.second })
    OutlinedCard(Modifier.fillMaxWidth()) {
        Column(Modifier.padding(20.dp), verticalArrangement = Arrangement.spacedBy(14.dp)) {
            Text("Where your time went", fontWeight = FontWeight.SemiBold)
            if (categories.isEmpty())
                Text(
                    "Use your apps to build a category breakdown.",
                    style = MaterialTheme.typography.bodySmall,
                )
            else {
                Row(Modifier.fillMaxWidth().height(12.dp).clip(RoundedCornerShape(8.dp))) {
                    categories.forEach { (name, ms) ->
                        Box(
                            Modifier.weight(ms.toFloat().coerceAtLeast(1f))
                                .fillMaxHeight()
                                .background(categoryColor(name))
                        )
                    }
                }
                categories.forEach { (name, ms) ->
                    Row(
                        verticalAlignment = Alignment.CenterVertically,
                        horizontalArrangement = Arrangement.spacedBy(10.dp),
                    ) {
                        Box(
                            Modifier.size(9.dp)
                                .clip(RoundedCornerShape(3.dp))
                                .background(categoryColor(name))
                        )
                        Text(name, Modifier.weight(1f), style = MaterialTheme.typography.bodyMedium)
                        Text(
                            "${ms*100/total}%",
                            style = MaterialTheme.typography.bodySmall,
                            color = MaterialTheme.colorScheme.onSurfaceVariant,
                        )
                        Text(usage(ms), fontWeight = FontWeight.Medium)
                    }
                }
            }
        }
    }
}

@Composable
internal fun WeekChart(state: UiState, onDate: (java.time.LocalDate) -> Unit) {
    val days =
        remember(state.history, state.date) {
            UsageAnalytics.daily(state.history, state.date.minusDays(6), 7, ZoneId.systemDefault())
        }
    val maximum = maxOf(60_000L, days.maxOfOrNull { it.second } ?: 0).toFloat()
    OutlinedCard(Modifier.fillMaxWidth()) {
        Column(Modifier.padding(20.dp), verticalArrangement = Arrangement.spacedBy(16.dp)) {
            Text("The last seven days", fontWeight = FontWeight.SemiBold)
            Row(
                Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.spacedBy(8.dp),
                verticalAlignment = Alignment.Bottom,
            ) {
                days.forEach { (date, ms) ->
                    Column(
                        Modifier.weight(1f).clickable { onDate(date) },
                        horizontalAlignment = Alignment.CenterHorizontally,
                        verticalArrangement = Arrangement.spacedBy(8.dp),
                    ) {
                        Box(
                            Modifier.height(80.dp).fillMaxWidth(),
                            contentAlignment = Alignment.BottomCenter,
                        ) {
                            Box(
                                Modifier.fillMaxWidth(.66f)
                                    .height((ms / maximum * 80).coerceAtLeast(4f).dp)
                                    .clip(RoundedCornerShape(6.dp))
                                    .background(
                                        if (date == state.date) MaterialTheme.colorScheme.primary
                                        else MaterialTheme.colorScheme.primary.copy(alpha = .4f)
                                    )
                            )
                        }
                        Text(
                            date.format(DateTimeFormatter.ofPattern("EEEEE")),
                            style = MaterialTheme.typography.labelSmall,
                        )
                    }
                }
            }
            Text(
                "Tap a day to explore · ${duration(days.sumOf { it.second })} saved",
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
        }
    }
}

@Composable
internal fun HistoryScreen(state: UiState, model: AppModel) {
    var range by rememberSaveable { mutableIntStateOf(7) }
    val from = state.date.minusDays(range.toLong() - 1)
    val zone = ZoneId.systemDefault()
    val boundary = from.atStartOfDay(zone).toInstant().toEpochMilli()
    val rows =
        remember(state.history, boundary) {
            state.history
                .filter { it.end > boundary }
                .map { it.copy(start = maxOf(it.start, boundary)) }
        }
    val total = remember(rows) { rows.sumOf { it.milliseconds } }
    val series = remember(rows, from, range, zone) { UsageAnalytics.daily(rows, from, range, zone) }
    val labels = remember(state.installed) { state.installed.associate { it.app to it.label } }
    val apps =
        remember(rows, labels, state.revision) {
            rows
                .groupBy { it.app }
                .map { (app, sessions) ->
                    AppUsage(
                        app,
                        labels[app] ?: app.substringAfterLast('.'),
                        sessions.sumOf { it.milliseconds },
                        sessions.size,
                        model.preferences.category(app),
                        model.preferences.limit(app),
                    )
                }
                .sortedByDescending { it.milliseconds }
        }
    Column(verticalArrangement = Arrangement.spacedBy(18.dp)) {
        SectionTitle("Your history.", "Trends across days, plus every visit in order.")
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            listOf(7, 30, 90).forEach { value ->
                FilterChip(
                    range == value,
                    onClick = { range = value },
                    label = { Text("$value days") },
                )
            }
        }
        Row(
            Modifier.height(IntrinsicSize.Min),
            horizontalArrangement = Arrangement.spacedBy(12.dp),
        ) {
            Metric("Total time", usage(total), "${apps.size} apps", Modifier.weight(1f))
            Metric("Daily average", usage(total / range), "Saved activity", Modifier.weight(1f))
        }
        WeekChart(state) { model.refresh(it) }
        CategoryChart(apps)
        Text(
            "Activity calendar",
            style = MaterialTheme.typography.titleMedium,
            fontWeight = FontWeight.SemiBold,
        )
        val primary = MaterialTheme.colorScheme.primary
        val background = MaterialTheme.colorScheme.surfaceContainerHighest
        val max = maxOf(1L, series.maxOfOrNull { it.second } ?: 1)
        Column(verticalArrangement = Arrangement.spacedBy(5.dp)) {
            series.chunked(14).forEach { chunk ->
                Row(horizontalArrangement = Arrangement.spacedBy(5.dp)) {
                    chunk.forEach { (date, ms) ->
                        Box(
                            Modifier.weight(1f)
                                .aspectRatio(1f)
                                .clip(RoundedCornerShape(4.dp))
                                .background(
                                    if (ms == 0L) background
                                    else primary.copy(alpha = .25f + .75f * ms / max)
                                )
                                .clickable { model.refresh(date) }
                                .semantics { contentDescription = "$date: ${duration(ms)}" }
                        )
                    }
                    repeat(14 - chunk.size) { Spacer(Modifier.weight(1f)) }
                }
            }
        }
        Text(
            "Empty cells mean no saved activity; Android may no longer have older events.",
            style = MaterialTheme.typography.bodySmall,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
        )
        TimelineScreen(state, model)
        WebsiteHistory(state.webHistory, state.date)
    }
}
