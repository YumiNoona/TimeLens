package `in`.venusapp.timelens.ui

import android.provider.Settings
import androidx.compose.foundation.layout.*
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import `in`.venusapp.timelens.data.*
import kotlinx.coroutines.delay
import java.time.LocalDate
import java.time.format.DateTimeFormatter

@Composable
internal fun SectionTitle(title: String, subtitle: String) { Column(verticalArrangement = Arrangement.spacedBy(6.dp)) { Text(title, style = MaterialTheme.typography.headlineSmall, fontWeight = FontWeight.Bold); Text(subtitle, color = MaterialTheme.colorScheme.onSurfaceVariant) } }

@Composable
internal fun InfoCard(title: String, description: String) {
    OutlinedCard(Modifier.fillMaxWidth()) { Column(Modifier.padding(20.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) { Text(title, fontWeight = FontWeight.SemiBold); Text(description, style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onSurfaceVariant) } }
}

@Composable
internal fun PermissionCard(open: () -> Unit) {
    Card(Modifier.fillMaxWidth()) { Column(Modifier.padding(20.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
        Icon(Icons.Outlined.PrivacyTip, null, tint = MaterialTheme.colorScheme.primary)
        Text("Make time visible", style = MaterialTheme.typography.titleLarge, fontWeight = FontWeight.Bold)
        Text("Allow Usage access so TimeLens can read which apps were in the foreground and for how long. Recent Android activity may be imported. Everything stays on this phone until you export it. You can revoke access at any time in Android Settings.")
        Button(onClick = open) { Text("Enable usage access") }
    } }
}

@Composable
internal fun EmptyActivity() = InfoCard("No recorded activity", "Enable usage access, use a few apps, then refresh. Android may delay recent events. Older history appears only if previously saved locally.")

@Composable
internal fun DateSelector(date: LocalDate, onDate: (LocalDate) -> Unit) {
    val context = LocalContext.current
    Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween, verticalAlignment = Alignment.CenterVertically) {
        IconButton(onClick = { onDate(date.minusDays(1)) }) { Icon(Icons.Outlined.ChevronLeft, "Previous day") }
        TextButton(onClick = {
            android.app.DatePickerDialog(context, { _, year, month, day -> onDate(LocalDate.of(year, month + 1, day)) }, date.year, date.monthValue - 1, date.dayOfMonth).apply { datePicker.maxDate = System.currentTimeMillis() }.show()
        }) { Icon(Icons.Outlined.CalendarMonth, null); Spacer(Modifier.width(8.dp)); Text(if (date == LocalDate.now()) "Today" else date.format(DateTimeFormatter.ofPattern("EEE, d MMM yyyy"))) }
        IconButton(onClick = { onDate(date.plusDays(1)) }, enabled = date < LocalDate.now()) { Icon(Icons.Outlined.ChevronRight, "Next day") }
    }
}

@Composable
internal fun SummaryCard(day: DayUsage, budget: Int) {
    Card(Modifier.fillMaxWidth(), colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.primaryContainer)) {
        Column(Modifier.padding(24.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
            Text("FOREGROUND TIME", style = MaterialTheme.typography.labelLarge)
            Text(duration(day.total), style = MaterialTheme.typography.displayMedium, fontWeight = FontWeight.Bold)
            val progress = (day.total / (budget * 60_000f)).coerceIn(0f, 1f)
            LinearProgressIndicator(progress = { progress }, modifier = Modifier.fillMaxWidth())
            Text("${duration(day.total)} of ${duration(budget * 60_000L)} daily budget · ${day.apps.size} apps · ${day.sessions.size} sessions", style = MaterialTheme.typography.bodySmall)
            if (progress >= 1) Text("Daily budget reached. A good moment for a break.", fontWeight = FontWeight.SemiBold)
        }
    }
}
