package `in`.venusapp.timelens.ui

import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.*
import androidx.compose.material3.*
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.semantics.*
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp

@Composable
internal fun HistoryRangeSelector(days: Int, onChange: (Int) -> Unit) {
    BoxWithConstraints(Modifier.fillMaxWidth()) {
        val short = maxWidth < 340.dp || LocalDensity.current.fontScale > 1.2f
        Surface(
            shape = RoundedCornerShape(16.dp),
            color = MaterialTheme.colorScheme.surfaceContainerHigh,
            border = BorderStroke(1.dp, MaterialTheme.colorScheme.outlineVariant),
        ) {
            Row(
                Modifier.fillMaxWidth().padding(4.dp),
                horizontalArrangement = Arrangement.spacedBy(4.dp),
            ) {
                listOf(7, 30, 90, 365).forEach { value ->
                    val label = if (value == 365) "1 year" else "$value days"
                    Surface(
                        onClick = { onChange(value) },
                        shape = RoundedCornerShape(12.dp),
                        modifier =
                            Modifier.weight(1f).testTag("history-range-$value").semantics {
                                contentDescription = label
                                role = Role.RadioButton
                                selected = days == value
                            },
                        color =
                            if (days == value) MaterialTheme.colorScheme.primary
                            else MaterialTheme.colorScheme.surfaceContainerHigh,
                        contentColor =
                            if (days == value) MaterialTheme.colorScheme.onPrimary
                            else MaterialTheme.colorScheme.onSurfaceVariant,
                    ) {
                        Box(
                            Modifier.fillMaxWidth()
                                .heightIn(min = 44.dp)
                                .padding(horizontal = 4.dp, vertical = 10.dp),
                            contentAlignment = Alignment.Center,
                        ) {
                            Text(
                                if (short) if (value == 365) "1yr" else "${value}d" else label,
                                style = MaterialTheme.typography.labelMedium,
                                fontWeight = FontWeight.SemiBold,
                                maxLines = 1,
                                softWrap = false,
                                overflow = TextOverflow.Clip,
                            )
                        }
                    }
                }
            }
        }
    }
}

private data class BlockDestination(
    val label: String,
    val description: String,
    val icon: ImageVector,
)

private val BlockDestinations =
    listOf(
        BlockDestination("Apps", "App rules", Icons.Outlined.Apps),
        BlockDestination("Web", "Website rules", Icons.Outlined.Language),
        BlockDestination("Activity", "Block activity", Icons.Outlined.History),
    )

@Composable
internal fun BlockSectionSelector(selected: Int, onChange: (Int) -> Unit) {
    Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(10.dp)) {
        BlockDestinations.forEachIndexed { index, item ->
            val active = selected == index
            OutlinedCard(
                onClick = { onChange(index) },
                modifier =
                    Modifier.weight(1f).semantics {
                        role = Role.Tab
                        this.selected = active
                        contentDescription = item.description
                    },
                border =
                    BorderStroke(
                        if (active) 1.5.dp else 1.dp,
                        if (active) MaterialTheme.colorScheme.primary
                        else MaterialTheme.colorScheme.outlineVariant,
                    ),
                colors =
                    CardDefaults.outlinedCardColors(
                        containerColor =
                            if (active) MaterialTheme.colorScheme.surfaceContainerHigh
                            else MaterialTheme.colorScheme.surface
                    ),
            ) {
                Column(
                    Modifier.fillMaxWidth().padding(vertical = 12.dp),
                    horizontalAlignment = Alignment.CenterHorizontally,
                    verticalArrangement = Arrangement.spacedBy(6.dp),
                ) {
                    Icon(item.icon, null, Modifier.size(22.dp))
                    Text(item.label, style = MaterialTheme.typography.labelLarge, maxLines = 1)
                }
            }
        }
    }
}

@Composable
internal fun DashboardSettings(model: AppModel) {
    val allowed = listOf("hourly", "apps")
    val current = model.preferences.cards.filter { it in allowed }
    Text(
        "Choose the modules shown below your daily summary.",
        style = MaterialTheme.typography.bodySmall,
    )
    (current + (allowed - current.toSet())).forEach { key ->
        val enabled = key in current
        val label = if (key == "hourly") "Hourly activity" else "Most used apps"
        Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
            Checkbox(
                enabled,
                onCheckedChange = { checked ->
                    model.setDashboardCards(if (checked) current + key else current - key)
                },
                modifier = Modifier.testTag("dashboard-$key"),
            )
            Text(label, Modifier.weight(1f), style = MaterialTheme.typography.bodyMedium)
            if (enabled) {
                val index = current.indexOf(key)
                IconButton(
                    enabled = index > 0,
                    onClick = {
                        val order = current.toMutableList()
                        java.util.Collections.swap(order, index, index - 1)
                        model.setDashboardCards(order)
                    },
                ) {
                    Icon(Icons.Outlined.KeyboardArrowUp, "Move $label up")
                }
                IconButton(
                    enabled = index < current.lastIndex,
                    onClick = {
                        val order = current.toMutableList()
                        java.util.Collections.swap(order, index, index + 1)
                        model.setDashboardCards(order)
                    },
                ) {
                    Icon(Icons.Outlined.KeyboardArrowDown, "Move $label down")
                }
            }
        }
    }
}

@Composable
internal fun ActionToggleRow(
    title: String,
    description: String,
    checked: Boolean,
    onChange: (Boolean) -> Unit,
) {
    Row(
        Modifier.fillMaxWidth().padding(vertical = 8.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(14.dp),
    ) {
        Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(5.dp)) {
            Text(title, style = MaterialTheme.typography.titleSmall)
            Text(
                description,
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
        }
        OutlinedButton(
            onClick = { onChange(!checked) },
            modifier = Modifier.semantics { stateDescription = if (checked) "On" else "Off" },
        ) {
            Icon(
                if (checked) Icons.Outlined.CheckCircle else Icons.Outlined.PowerSettingsNew,
                null,
                Modifier.size(18.dp),
            )
            Spacer(Modifier.width(6.dp))
            Text(if (checked) "Turn off" else "Turn on", maxLines = 1)
        }
    }
}
