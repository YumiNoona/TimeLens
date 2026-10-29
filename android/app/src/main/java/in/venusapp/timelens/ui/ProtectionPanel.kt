package `in`.venusapp.timelens.ui

import android.content.Intent
import android.provider.Settings
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.layout.*
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.compose.LifecycleEventEffect
import `in`.venusapp.timelens.platform.*
import kotlinx.coroutines.*

@Composable
internal fun ProtectionPanel(model: AppModel, guard: (() -> Unit) -> Unit) {
    val context = LocalContext.current
    val controller = remember { RemovalProtection(context.applicationContext) }
    val scope = rememberCoroutineScope()
    var status by remember { mutableStateOf<RemovalStatus?>(null) }
    var revision by remember { mutableIntStateOf(0) }
    var error by remember { mutableStateOf<String?>(null) }
    var confirm by remember { mutableStateOf(false) }
    var busy by remember { mutableStateOf(false) }
    LifecycleEventEffect(Lifecycle.Event.ON_RESUME) { revision++ }
    LaunchedEffect(revision) {
        status = withContext(Dispatchers.IO) { runCatching { controller.status() }.getOrNull() }
    }
    val activation =
        rememberLauncherForActivityResult(ActivityResultContracts.StartActivityForResult()) {
            revision++
        }
    fun update(action: () -> Unit) {
        scope.launch {
            busy = true
            error = null
            try {
                status =
                    withContext(Dispatchers.IO) {
                        action()
                        controller.status()
                    }
            } catch (_: Exception) {
                error =
                    "Android could not apply this change. Open its administration settings to review protection."
            } finally {
                busy = false
            }
        }
    }
    Row(
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(12.dp),
    ) {
        Icon(
            if (AppBlockService.connected) Icons.Outlined.VerifiedUser else Icons.Outlined.GppMaybe,
            null,
        )
        Text(
            if (AppBlockService.connected) "Blocking service connected"
            else "Blocking service needs attention"
        )
    }
    Text(
        if (model.preferences.pinHash.isNotEmpty()) "Blocking PIN is set"
        else "Set a blocking PIN to protect changes made inside TimeLens.",
        style = MaterialTheme.typography.bodySmall,
    )
    val policy = status
    if (policy == null)
        Text("Checking Android protection…", style = MaterialTheme.typography.bodySmall)
    else if (policy.managed) {
        Text("Android-managed device", style = MaterialTheme.typography.titleMedium)
        Text(
            "TimeLens is an authorized device or profile owner. Android can enforce uninstall blocking in this managed scope. Your administrator also controls removal.",
            style = MaterialTheme.typography.bodySmall,
        )
        SettingSwitch(
            "Prevent uninstall",
            "Enforced by Android device policy",
            policy.uninstallBlocked,
        ) { enabled ->
            guard {
                if (enabled) confirm = true else update { controller.setManagedBlock(false) }
            }
        }
        if (policy.uninstallBlocked)
            OutlinedButton(
                enabled = !busy,
                onClick = { guard { update { controller.setManagedBlock(false) } } },
                modifier = Modifier.fillMaxWidth(),
            ) {
                Text("Release uninstall policy")
            }
        Text(
            "Turning this policy off does not remove device or profile ownership. The provisioning administrator may still need to release the device before uninstalling.",
            style = MaterialTheme.typography.bodySmall,
        )
    } else {
        Text(
            if (policy.administrator) "Extra removal step active"
            else "Optional extra removal step",
            style = MaterialTheme.typography.titleMedium,
        )
        Text(
            "On a normal phone, Android can require deactivating this administrator before uninstalling. The phone owner can still do both in Android Settings. This is removal friction, not an uninstall-proof lock.",
            style = MaterialTheme.typography.bodySmall,
        )
        if (policy.administrator)
            OutlinedButton(
                enabled = !busy,
                onClick = { guard { update { controller.removePersonalAdministrator() } } },
                modifier = Modifier.fillMaxWidth(),
            ) {
                Text("Turn off extra removal step")
            }
        else
            OutlinedButton(
                enabled = !busy,
                onClick = {
                    guard {
                        runCatching { activation.launch(controller.activationIntent()) }
                            .onFailure {
                                error =
                                    "This Android version does not expose the administration flow."
                            }
                    }
                },
                modifier = Modifier.fillMaxWidth(),
            ) {
                Icon(Icons.Outlined.Security, null)
                Spacer(Modifier.width(8.dp))
                Text("Add extra removal step")
            }
        Text(
            "True uninstall blocking requires an independently provisioned managed device. TimeLens does not enroll or reset your phone automatically.",
            style = MaterialTheme.typography.bodySmall,
        )
    }
    error?.let { Text(it, style = MaterialTheme.typography.bodySmall) }
    TextButton(
        onClick = {
            runCatching { context.startActivity(Intent(Settings.ACTION_SECURITY_SETTINGS)) }
        }
    ) {
        Text("Open Android security settings")
    }
    if (confirm)
        AlertDialog(
            onDismissRequest = { confirm = false },
            title = { Text("Block uninstall on this managed device?") },
            text = {
                Text(
                    "Android will reject uninstalling TimeLens in this managed scope. Release protection here or through your authorized administrator before removal. This action does not factory-reset or enroll the device."
                )
            },
            confirmButton = {
                TextButton(
                    onClick = {
                        confirm = false
                        update { controller.setManagedBlock(true) }
                    }
                ) {
                    Text("Enable protection")
                }
            },
            dismissButton = { TextButton(onClick = { confirm = false }) { Text("Cancel") } },
        )
}
