package `in`.venusapp.timelens.ui

import androidx.compose.foundation.layout.*
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.unit.dp
import `in`.venusapp.timelens.data.Preferences
import `in`.venusapp.timelens.domain.PinSecurity
import kotlinx.coroutines.*

@Composable
internal fun PinChallenge(prefs: Preferences, onVerified: () -> Unit, onDismiss: () -> Unit) {
    var pin by remember { mutableStateOf("") }
    var message by remember { mutableStateOf("") }
    var busy by remember { mutableStateOf(false) }
    val scope = rememberCoroutineScope()
    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text("Unlock blocking controls") },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(12.dp)) {
                Text("Enter your blocking PIN to change or pause protection.")
                OutlinedTextField(
                    pin,
                    { pin = it.filter(Char::isDigit).take(12) },
                    label = { Text("PIN") },
                    visualTransformation = PasswordVisualTransformation(),
                    keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.NumberPassword),
                    singleLine = true,
                )
                if (message.isNotEmpty()) Text(message, color = MaterialTheme.colorScheme.error)
            }
        },
        confirmButton = {
            TextButton(
                enabled = !busy && pin.length >= 4,
                onClick = {
                    if (System.currentTimeMillis() < prefs.pinRetryAt) {
                        message = "Please wait a minute before trying again."
                        return@TextButton
                    }
                    busy = true
                    scope.launch {
                        val accepted =
                            withContext(Dispatchers.Default) {
                                PinSecurity.verify(pin, prefs.pinHash)
                            }
                        busy = false
                        if (accepted) {
                            prefs.pinFailures = 0
                            onVerified()
                        } else {
                            prefs.pinFailures += 1
                            if (prefs.pinFailures >= 5) {
                                prefs.pinRetryAt = System.currentTimeMillis() + 60_000
                                prefs.pinFailures = 0
                            }
                            message = "Incorrect PIN"
                        }
                    }
                },
            ) {
                Text(if (busy) "Checking…" else "Unlock")
            }
        },
        dismissButton = { TextButton(onClick = onDismiss) { Text("Cancel") } },
    )
}
