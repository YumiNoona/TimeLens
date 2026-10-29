package `in`.venusapp.timelens.platform

import android.app.UiAutomation
import android.content.ComponentName
import android.content.pm.ApplicationInfo
import android.os.ParcelFileDescriptor
import androidx.test.platform.app.InstrumentationRegistry
import org.junit.Assert.*
import org.junit.Assume.assumeTrue
import org.junit.Test

/**
 * Uses a testOnly target on a dedicated, owner-free emulator; always removes its temporary role.
 */
class RemovalProtectionTest {
    @Test
    fun normalInstallNeverPretendsItCanEnforceAnUninstallBlock() {
        val context = InstrumentationRegistry.getInstrumentation().targetContext
        val controller = RemovalProtection(context)
        assumeTrue(!controller.status().managed)
        assertFalse(controller.status().uninstallBlocked)
        assertEquals(
            android.app.admin.DevicePolicyManager.ACTION_ADD_DEVICE_ADMIN,
            controller.activationIntent().action,
        )
        try {
            controller.setManagedBlock(true)
            fail("An ordinary install must not claim owner privileges")
        } catch (_: IllegalStateException) {}
    }

    @Test
    fun authorizedOwnerCanBlockUninstallAndReleaseIt() {
        assumeTrue(
            InstrumentationRegistry.getArguments().getString("allowManagedPolicyTest") == "true"
        )
        assumeTrue(android.os.Build.HARDWARE in listOf("ranchu", "goldfish"))
        val instrumentation = InstrumentationRegistry.getInstrumentation()
        val context = instrumentation.targetContext
        assumeTrue(context.applicationInfo.flags and ApplicationInfo.FLAG_TEST_ONLY != 0)
        val automation =
            instrumentation.getUiAutomation(UiAutomation.FLAG_DONT_SUPPRESS_ACCESSIBILITY_SERVICES)
        fun shell(command: String) =
            ParcelFileDescriptor.AutoCloseInputStream(automation.executeShellCommand(command))
                .bufferedReader()
                .use { it.readText() }
        assumeTrue(shell("dpm list-owners").contains("no owners", ignoreCase = true))
        val component = ComponentName(context, TimeLensAdminReceiver::class.java).flattenToString()
        val controller = RemovalProtection(context)
        try {
            val result = shell("dpm set-device-owner $component")
            assertTrue(result, result.contains("Success", ignoreCase = true))
            assertTrue(controller.status().managed)
            assertTrue(controller.setManagedBlock(true).uninstallBlocked)
            val uninstall = shell("pm uninstall --user 0 ${context.packageName}")
            assertTrue(
                "Android must reject removal while policy is active: $uninstall",
                uninstall.contains("Failure"),
            )
            assertFalse(controller.setManagedBlock(false).uninstallBlocked)
        } finally {
            if (controller.status().managed) controller.setManagedBlock(false)
            val removal = shell("dpm remove-active-admin $component")
            assertFalse(
                "Temporary owner must be recoverable: $removal",
                controller.status().managed,
            )
        }
    }
}
