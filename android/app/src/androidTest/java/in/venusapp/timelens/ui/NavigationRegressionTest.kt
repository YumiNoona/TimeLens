package `in`.venusapp.timelens.ui

import androidx.compose.ui.semantics.SemanticsProperties
import androidx.compose.ui.test.*
import androidx.compose.ui.test.junit4.createAndroidComposeRule
import `in`.venusapp.timelens.MainActivity
import org.junit.Assert.*
import org.junit.Rule
import org.junit.Test

/** Real Compose actions reproduce the reported refresh/toggle/navigation regression. */
class NavigationRegressionTest {
    @get:Rule val compose = createAndroidComposeRule<MainActivity>()

    private fun scroll(page: Int) =
        compose
            .onNodeWithTag("page-$page")
            .fetchSemanticsNode()
            .config[SemanticsProperties.VerticalScrollAxisRange]
            .value()

    @Test
    fun settingToggleKeepsPageSectionAndScrollAcrossRefresh() {
        compose.onNodeWithContentDescription("Settings").performClick()
        compose.onNodeWithText("Appearance").performClick()
        compose.onNodeWithTag("switch-24-hour clock").performScrollTo()
        val before = scroll(4)
        compose.onNodeWithTag("switch-24-hour clock").performClick()
        compose.waitForIdle()
        compose.onNodeWithTag("page-4").assertExists()
        compose.onNodeWithTag("switch-24-hour clock").assertIsDisplayed()
        assertTrue(
            "A setting must not jump to the bottom",
            kotlin.math.abs(before - scroll(4)) < 20f,
        )
        compose.onNodeWithContentDescription("Refresh activity").performClick()
        compose.waitForIdle()
        assertTrue(kotlin.math.abs(before - scroll(4)) < 20f)
    }

    @Test
    fun destinationsKeepSeparateScrollAndSavedSearch() {
        compose.onNodeWithContentDescription("Settings").performClick()
        compose.onNodeWithText("Delete all saved phone activity").performScrollTo()
        val settings = scroll(4)
        compose.onNodeWithContentDescription("Apps", useUnmergedTree = true).performClick()
        assertEquals(0f, scroll(1), 1f)
        compose.onNodeWithContentDescription("Search apps").performClick()
        compose.onNodeWithText("Search name or package").performTextInput("nonexistent-app")
        compose.onNodeWithContentDescription("History", useUnmergedTree = true).performClick()
        compose.onNodeWithTag("page-2").assertExists()
        compose.onNodeWithContentDescription("Apps", useUnmergedTree = true).performClick()
        compose.onNodeWithText("nonexistent-app").assertExists()
        compose.onNodeWithContentDescription("Settings").performClick()
        assertEquals(settings, scroll(4), 2f)
    }

    @Test
    fun filterDialogAndBlockTabsDoNotNavigateAway() {
        compose.onNodeWithContentDescription("Apps", useUnmergedTree = true).performClick()
        compose.onNodeWithContentDescription("Filter apps").performClick()
        compose.onNodeWithText("Installed").performClick()
        compose.onNodeWithText("Apply").performClick()
        compose.onNodeWithText("Installed on this phone").assertExists()
        compose.onNodeWithContentDescription("Block", useUnmergedTree = true).performClick()
        compose.onNodeWithContentDescription("Website rules").performClick()
        compose.onNodeWithTag("page-3").assertExists()
        compose.onNodeWithContentDescription("Block activity").performClick()
        compose.onNodeWithTag("page-3").assertExists()
        compose.onNodeWithText("Timer").assertDoesNotExist()
    }
}
