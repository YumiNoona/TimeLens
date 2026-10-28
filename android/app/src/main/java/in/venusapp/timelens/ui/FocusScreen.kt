package `in`.venusapp.timelens.ui

import androidx.compose.foundation.layout.*
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import androidx.lifecycle.Lifecycle
import `in`.venusapp.timelens.data.*
import kotlinx.coroutines.delay
import java.util.Locale

@Composable
internal fun FocusScreen(prefs: Preferences, revision: Int) {
    var end by remember(revision) { mutableLongStateOf(prefs.focusEnd) }
    var now by remember { mutableLongStateOf(System.currentTimeMillis()) }
    var minutes by rememberSaveable { mutableIntStateOf(25) }
    val lifecycle = androidx.lifecycle.compose.LocalLifecycleOwner.current.lifecycle
    val lifecycleState by lifecycle.currentStateFlow.collectAsState()
    LaunchedEffect(end, lifecycleState) {
        if (lifecycleState.isAtLeast(Lifecycle.State.RESUMED)) while (true) { now = System.currentTimeMillis(); delay(1000) }
    }
    val remaining = (end - now).coerceAtLeast(0)
    Column(verticalArrangement = Arrangement.spacedBy(20.dp), horizontalAlignment = Alignment.CenterHorizontally, modifier = Modifier.fillMaxWidth()) {
        SectionTitle("Room to focus.", "One intention. A little uninterrupted time.")
        Icon(Icons.Outlined.Spa, null, tint = MaterialTheme.colorScheme.primary, modifier = Modifier.size(56.dp))
        Text(if (end > 0 && remaining == 0L) "Session complete" else if (remaining > 0) String.format(Locale.ROOT, "%02d:%02d", remaining / 60_000, (remaining / 1000) % 60) else "$minutes:00", style = MaterialTheme.typography.displayLarge)
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) { listOf(15, 25, 50).forEach { value -> FilterChip(selected = minutes == value, enabled = remaining == 0L, onClick = { minutes = value }, label = { Text("${value}m") }) } }
        Button(onClick = { end = if (remaining > 0) 0 else System.currentTimeMillis() + minutes * 60_000L; prefs.focusEnd = end }, modifier = Modifier.fillMaxWidth()) { Text(if (remaining > 0) "End session" else "Start focus session") }
        InfoCard("A gentle focus timer", "The deadline survives app restarts. This preview has no background alarm or app blocking; reopen Focus to see completion. For stronger limits, use Android Digital Wellbeing while enforcement is developed.")
    }
}
