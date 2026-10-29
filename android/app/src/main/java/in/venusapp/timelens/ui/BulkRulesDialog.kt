package `in`.venusapp.timelens.ui

import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.unit.dp
import `in`.venusapp.timelens.domain.BlockRule

@Composable
internal fun BulkRulesDialog(
    state: UiState,
    safe: Set<String>,
    favorites: Set<String>,
    onDismiss: () -> Unit,
    onSave: (List<BlockRule>) -> Unit,
) {
    var selected by rememberSaveable { mutableStateOf(emptyList<String>()) }
    var search by rememberSaveable { mutableStateOf("") }
    var mode by rememberSaveable { mutableStateOf("comfort") }
    var minutes by rememberSaveable { mutableStateOf("30") }
    val apps = remember(state.installed, safe) { state.installed.filter { it.app !in safe } }
    val visible =
        remember(apps, search) {
            apps.filter { it.label.contains(search, true) || it.app.contains(search, true) }
        }
    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text("Create rules together") },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(10.dp)) {
                Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    FilterChip(
                        mode == "comfort",
                        onClick = { mode = "comfort" },
                        label = { Text("Comfort") },
                    )
                    FilterChip(
                        mode == "strict",
                        onClick = { mode = "strict" },
                        label = { Text("Strict") },
                    )
                }
                if (mode == "comfort")
                    OutlinedTextField(
                        minutes,
                        { minutes = it.filter(Char::isDigit).take(4) },
                        singleLine = true,
                        label = { Text("Minutes per app / rolling 24h") },
                        keyboardOptions =
                            KeyboardOptions(
                                keyboardType = KeyboardType.Number,
                                imeAction = ImeAction.Done,
                            ),
                        keyboardActions = dismissKeyboardActions(),
                        isError = minutes.toIntOrNull()?.let { it in 1..1440 } != true,
                        modifier = Modifier.fillMaxWidth(),
                    )
                Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    TextButton(
                        onClick = { selected = apps.filter { it.app in favorites }.map { it.app } }
                    ) {
                        Text("Favorites")
                    }
                    TextButton(
                        onClick = {
                            selected =
                                apps
                                    .filter {
                                        state.appCategories[it.app] in
                                            setOf("Social", "Entertainment", "Gaming")
                                    }
                                    .map { it.app }
                        }
                    ) {
                        Text("Distractions")
                    }
                    TextButton(onClick = { selected = emptyList() }) { Text("Clear") }
                }
                SearchInput(
                    search,
                    { search = it },
                    label = "Find apps",
                )
                Text(
                    "${selected.size} selected · existing rules for these apps will be replaced",
                    style = MaterialTheme.typography.bodySmall,
                )
                LazyColumn(Modifier.heightIn(max = 300.dp)) {
                    items(visible, key = { it.app }) { app ->
                        fun toggle() {
                            selected =
                                if (app.app in selected) selected - app.app else selected + app.app
                        }
                        Row(
                            Modifier.fillMaxWidth().clickable { toggle() }.padding(vertical = 6.dp),
                            verticalAlignment = Alignment.CenterVertically,
                            horizontalArrangement = Arrangement.spacedBy(10.dp),
                        ) {
                            Checkbox(app.app in selected, onCheckedChange = { toggle() })
                            AppIcon(app.app, app.label, Modifier.size(32.dp))
                            Text(app.label, Modifier.weight(1f))
                        }
                    }
                }
            }
        },
        confirmButton = {
            TextButton(
                enabled =
                    selected.isNotEmpty() &&
                        (mode == "strict" || minutes.toIntOrNull()?.let { it in 1..1440 } == true),
                onClick = {
                    onSave(
                        selected.map {
                            BlockRule(
                                it,
                                mode,
                                allowanceMinutes = minutes.toIntOrNull()?.coerceIn(1, 1440) ?: 30,
                            )
                        }
                    )
                },
            ) {
                Text("Save ${selected.size} ${if(selected.size==1) "rule" else "rules"}")
            }
        },
        dismissButton = { TextButton(onClick = onDismiss) { Text("Cancel") } },
    )
}
