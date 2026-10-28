package `in`.venusapp.timelens.ui

import androidx.compose.foundation.layout.*
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import `in`.venusapp.timelens.domain.UsageAnalytics
import java.time.LocalDate

@Composable
internal fun TodayScreen(state: UiState, model: AppModel, onApps: () -> Unit, onFocus: () -> Unit) {
    val day = state.day ?: return
    val prefs = model.preferences
    val productive =
        day.apps.filter { it.category in prefs.productiveCategories }.sumOf { it.milliseconds }
    val score = if (day.total == 0L) 0 else (productive * 100 / day.total).toInt()
    val yesterdayStart =
        state.date
            .minusDays(1)
            .atStartOfDay(java.time.ZoneId.systemDefault())
            .toInstant()
            .toEpochMilli()
    val todayStart =
        state.date.atStartOfDay(java.time.ZoneId.systemDefault()).toInstant().toEpochMilli()
    val yesterday =
        state.history.sumOf {
            (minOf(it.end, todayStart) - maxOf(it.start, yesterdayStart)).coerceAtLeast(0)
        }
    var customize by remember { mutableStateOf(false) }
    Column(verticalArrangement = Arrangement.spacedBy(18.dp)) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Column(Modifier.weight(1f)) {
                Text(
                    if (state.date == LocalDate.now()) "Own your attention."
                    else "A day in perspective.",
                    style = MaterialTheme.typography.headlineMedium,
                    fontWeight = FontWeight.Bold,
                )
                Text(
                    "Small habits. More intentional days.",
                    style = MaterialTheme.typography.bodyMedium,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }
            IconButton(onClick = { customize = true }) {
                Icon(Icons.Outlined.DashboardCustomize, "Customize dashboard")
            }
        }
        SummaryCard(day, prefs.budget)
        Row(
            Modifier.height(IntrinsicSize.Min),
            horizontalArrangement = Arrangement.spacedBy(12.dp),
        ) {
            Metric("Focus score", "$score%", "Your productive categories", Modifier.weight(1f))
            Metric(
                "App switches",
                UsageAnalytics.switches(day.sessions).toString(),
                "${day.sessions.size} sessions",
                Modifier.weight(1f).fillMaxHeight(),
            )
        }
        Row(
            Modifier.height(IntrinsicSize.Min),
            horizontalArrangement = Arrangement.spacedBy(12.dp),
        ) {
            Metric(
                "Longest session",
                usage(UsageAnalytics.longest(day.sessions)),
                "Continuous foreground use",
                Modifier.weight(1f).fillMaxHeight(),
            )
            Metric(
                "Yesterday",
                usage(yesterday),
                if (yesterday > 0)
                    "${if(day.total>=yesterday) "+" else "−"}${duration(kotlin.math.abs(day.total-yesterday))} difference"
                else "No comparison saved",
                Modifier.weight(1f).fillMaxHeight(),
            )
        }
        if (prefs.pauseTrackingUntil > System.currentTimeMillis())
            InfoCard(
                "Tracking paused",
                "Activity during this pause is excluded from later imports too.",
            )
        prefs.cards.forEach { card ->
            when (card) {
                "hourly" -> HourlyChart(day)
                "categories" -> Unit
                "week" -> Unit
                "apps" -> {
                    SectionTitle("Most used", "Your top apps, with categories and daily limits")
                    day.apps.take(5).forEach { AppCard(it, model) }
                    OutlinedButton(onClick = onApps, modifier = Modifier.fillMaxWidth()) {
                        Text("Explore all apps")
                    }
                }
            }
        }
        if (day.sessions.isEmpty()) EmptyActivity()
        Text(
            "${state.blocksToday} blocked openings today",
            style = MaterialTheme.typography.bodySmall,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
        )
    }
    if (customize)
        AlertDialog(
            onDismissRequest = { customize = false },
            title = { Text("Your dashboard") },
            text = {
                Column {
                    listOf("hourly", "apps").forEach { key ->
                        val enabled = key in prefs.cards
                        Row(verticalAlignment = Alignment.CenterVertically) {
                            Checkbox(
                                enabled,
                                onCheckedChange = { selected ->
                                    model.edit {
                                        it.preferences.cards =
                                            if (selected) prefs.cards + key else prefs.cards - key
                                    }
                                },
                            )
                            Text(key.replaceFirstChar(Char::uppercase), Modifier.weight(1f))
                            if (enabled)
                                IconButton(
                                    onClick = {
                                        val order = prefs.cards.toMutableList()
                                        val index = order.indexOf(key)
                                        if (index > 0) {
                                            java.util.Collections.swap(order, index, index - 1)
                                            model.edit { it.preferences.cards = order }
                                        }
                                    }
                                ) {
                                    Icon(Icons.Outlined.ArrowUpward, "Move $key up")
                                }
                        }
                    }
                }
            },
            confirmButton = { TextButton(onClick = { customize = false }) { Text("Done") } },
        )
}
