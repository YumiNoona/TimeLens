package `in`.venusapp.timelens.platform

import android.content.Context
import android.content.Intent

object AppSafety {
    fun protectedPackages(context: Context): Set<String> {
        val home =
            context.packageManager
                .resolveActivity(Intent(Intent.ACTION_MAIN).addCategory(Intent.CATEGORY_HOME), 0)
                ?.activityInfo
                ?.packageName
        val settings =
            context.packageManager
                .resolveActivity(Intent(android.provider.Settings.ACTION_SETTINGS), 0)
                ?.activityInfo
                ?.packageName
        val dialer =
            context
                .getSystemService(android.telecom.TelecomManager::class.java)
                ?.defaultDialerPackage
        return setOfNotNull(
            context.packageName,
            "android",
            "com.android.systemui",
            "com.android.settings",
            "com.android.permissioncontroller",
            "com.google.android.permissioncontroller",
            "com.android.phone",
            "com.android.server.telecom",
            home,
            settings,
            dialer,
        )
    }
}
