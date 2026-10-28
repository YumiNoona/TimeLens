package `in`.venusapp.timelens.domain

import org.junit.Assert.*
import org.junit.Test

class ThemeModeTest {
    @Test fun followsSystemUnlessExplicitlyOverridden() {
        assertTrue(ThemeMode.isDark("system", true))
        assertFalse(ThemeMode.isDark("system", false))
        assertTrue(ThemeMode.isDark("dark", false))
        assertFalse(ThemeMode.isDark("light", true))
    }
    @Test fun missingAndLegacyValuesFallBackToSystem() {
        assertEquals("system", ThemeMode.normalize(null))
        assertEquals("system", ThemeMode.normalize("acid"))
    }
}
