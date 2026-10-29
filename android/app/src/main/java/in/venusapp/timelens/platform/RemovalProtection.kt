package `in`.venusapp.timelens.platform

import android.app.admin.DeviceAdminReceiver
import android.app.admin.DevicePolicyManager
import android.content.ComponentName
import android.content.Context
import android.content.Intent

data class RemovalStatus(
    val administrator: Boolean = false,
    val managed: Boolean = false,
    val uninstallBlocked: Boolean = false,
)

/** Uses Android's explicit administration flow. Never intercepts Settings or uninstall screens. */
class RemovalProtection(private val context: Context) {
    private val manager = context.getSystemService(DevicePolicyManager::class.java)
    private val admin = ComponentName(context, TimeLensAdminReceiver::class.java)

    fun status(): RemovalStatus {
        val active = manager.isAdminActive(admin)
        val managed =
            manager.isDeviceOwnerApp(context.packageName) ||
                manager.isProfileOwnerApp(context.packageName)
        val blocked = if (managed) manager.isUninstallBlocked(admin, context.packageName) else false
        return RemovalStatus(active, managed, blocked)
    }

    fun activationIntent() =
        Intent(DevicePolicyManager.ACTION_ADD_DEVICE_ADMIN)
            .putExtra(DevicePolicyManager.EXTRA_DEVICE_ADMIN, admin)
            .putExtra(
                DevicePolicyManager.EXTRA_ADD_EXPLANATION,
                "Add an extra Android confirmation step before removal. TimeLens requests no password, lock-screen or device-wipe policies. You can deactivate it in Android Settings.",
            )

    fun setManagedBlock(enabled: Boolean): RemovalStatus {
        check(status().managed) {
            "Android-enforced uninstall blocking requires authorized device or profile ownership."
        }
        manager.setUninstallBlocked(admin, context.packageName, enabled)
        return status().also {
            check(it.uninstallBlocked == enabled) { "Android did not apply this policy." }
        }
    }

    fun removePersonalAdministrator() {
        check(!status().managed) {
            "Use the managed-device release control or contact your administrator."
        }
        manager.removeActiveAdmin(admin)
    }
}

class TimeLensAdminReceiver : DeviceAdminReceiver() {
    override fun onDisableRequested(context: Context, intent: Intent): CharSequence =
        "The extra TimeLens removal step will be disabled. You can then uninstall TimeLens using Android Settings."
}
