package `in`.venusapp.timelens.data

import android.content.Context
import android.content.ContextWrapper
import androidx.test.platform.app.InstrumentationRegistry
import org.junit.Assert.*
import org.junit.Test

class PreferencesMigrationTest {
    private val context =
        object : ContextWrapper(InstrumentationRegistry.getInstrumentation().targetContext) {
            override fun getApplicationContext(): Context = this

            override fun getSharedPreferences(name: String, mode: Int) =
                super.getSharedPreferences("migration_test_only", mode)
        }

    @Test
    fun migratesModesWithoutResettingExistingPrivacyOrProtection() {
        val raw = context.getSharedPreferences("timelens", 0)
        raw.edit()
            .clear()
            .putLong("since", 1234)
            .putString("pinHash", "existing-protected-hash")
            .putLong("focusEnd", 99999)
            .putString("theme", "system")
            .putInt("limit.budget.app", 45)
            .putString(
                "blockRules",
                """[{"app":"strict.app","mode":"always"},{"app":"budget.app","mode":"budget"},{"app":"focus.app","mode":"focus"},{"app":"schedule.app","mode":"schedule"}]""",
            )
            .commit()
        try {
            val prefs = Preferences(context)
            assertEquals(
                listOf("strict", "comfort", "comfort", "comfort"),
                prefs.blockRules.map { it.mode },
            )
            assertTrue(prefs.blockRules[0].enabled)
            assertTrue(prefs.blockRules[1].enabled)
            assertEquals(45, prefs.blockRules[1].allowanceMinutes)
            assertFalse(prefs.blockRules[2].enabled)
            assertFalse(prefs.blockRules[3].enabled)
            assertEquals(1234L, prefs.since)
            assertEquals("existing-protected-hash", prefs.pinHash)
            assertEquals(0L, prefs.focusEnd)
            assertEquals("dark", prefs.theme)
            prefs.focusEnd = 88888
            assertEquals("Migration must run only once", 88888L, Preferences(context).focusEnd)
        } finally {
            raw.edit().clear().commit()
        }
    }

    @Test
    fun aDisabledDailyBudgetMustNotBecomeANewThirtyMinuteRestriction() {
        val raw = context.getSharedPreferences("timelens", 0)
        raw.edit()
            .clear()
            .putString("blockRules", """[{"app":"unlimited.app","mode":"budget"}]""")
            .commit()
        try {
            val rule = Preferences(context).blockRules.single()
            assertEquals("comfort", rule.mode)
            assertFalse(rule.enabled)
        } finally {
            raw.edit().clear().commit()
        }
    }
}
