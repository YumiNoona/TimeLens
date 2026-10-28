package `in`.venusapp.timelens.ui

import androidx.compose.foundation.layout.*
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Language
import androidx.compose.material3.*
import androidx.compose.runtime.Composable
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import `in`.venusapp.timelens.domain.Session
import java.time.LocalDate
import java.time.ZoneId

@Composable
internal fun WebsiteHistory(history: List<Session>, date: LocalDate) {
    val from = date.atStartOfDay(ZoneId.systemDefault()).toInstant().toEpochMilli()
    val until = date.plusDays(1).atStartOfDay(ZoneId.systemDefault()).toInstant().toEpochMilli()
    val rows =
        remember(history, from, until) {
            history
                .filter { it.end > from && it.start < until }
                .map { it.copy(start = maxOf(it.start, from), end = minOf(it.end, until)) }
                .groupBy { it.app }
                .map { it.key to it.value.sumOf { row -> row.milliseconds } }
                .sortedByDescending { it.second }
        }
    SectionTitle(
        "Website history",
        "Observed domains on the selected day · included in browser app time",
    )
    if (rows.isEmpty())
        InfoCard(
            "No website observations",
            "Enable optional website tracking from Block → Websites. Hidden browser bars and unsupported browsers leave gaps.",
        )
    rows.take(80).forEach { (host, ms) ->
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
