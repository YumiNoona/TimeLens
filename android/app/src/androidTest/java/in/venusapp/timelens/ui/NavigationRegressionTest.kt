package `in`.venusapp.timelens.ui

import androidx.compose.ui.semantics.SemanticsProperties
import androidx.compose.ui.test.*
import androidx.compose.ui.test.junit4.createAndroidComposeRule
import `in`.venusapp.timelens.BuildConfig
import `in`.venusapp.timelens.MainActivity
import org.junit.Assert.*
import org.junit.Rule
import org.junit.Test

/** Real Compose actions reproduce the reported refresh/toggle/navigation regression. */
class NavigationRegressionTest {
    @get:Rule val compose = createAndroidComposeRule<MainActivity>()

    @Test
    fun yearReportLoadsAndBuildLabelsAreNotShown() {
        compose.onNodeWithContentDescription("History", useUnmergedTree = true).performClick()
        compose.onNodeWithTag("history-range-365").performClick()
        val model = androidx.lifecycle.ViewModelProvider(compose.activity)[AppModel::class.java]
        compose.waitUntil(15_000) {
            model.state.value.report?.days == 365 && !model.state.value.loading
        }
        compose.onNodeWithTag("history-range-365").assertIsSelected()
        assertEquals(365, model.state.value.report?.daily?.size)
        compose.onAllNodes(hasText("ON THIS PHONE", substring = true)).assertCountEquals(0)
        compose.onNodeWithContentDescription("Settings", useUnmergedTree = true).performClick()
        compose
            .onAllNodes(hasText("ANDROID ${BuildConfig.VERSION_NAME}", substring = true))
            .assertCountEquals(0)
        compose
            .onAllNodes(hasText("Android ${BuildConfig.VERSION_NAME}", substring = true))
            .assertCountEquals(0)
    }

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
        compose.onNodeWithContentDescription("Refresh activity").assertDoesNotExist()
        compose
            .onNode(
                hasContentDescription("Switch to light mode") or
                    hasContentDescription("Switch to dark mode")
            )
            .performClick()
        compose.waitForIdle()
        assertTrue(kotlin.math.abs(before - scroll(4)) < 20f)
    }

    @Test
    fun topBarThemeTogglePersistsWithoutDuplicateAppearanceControls() {
        compose.onNodeWithContentDescription("Settings").performClick()
        compose.onNodeWithText("Appearance").performClick()
        compose.onNodeWithText("Follow system").assertDoesNotExist()
        compose.onNodeWithText("Light").assertDoesNotExist()
        compose.onNodeWithText("Dark").assertDoesNotExist()
        val model = androidx.lifecycle.ViewModelProvider(compose.activity)[AppModel::class.java]
        val toLight =
            compose
                .onAllNodesWithContentDescription("Switch to light mode")
                .fetchSemanticsNodes()
                .isNotEmpty()
        compose
            .onNodeWithContentDescription(
                if (toLight) "Switch to light mode" else "Switch to dark mode"
            )
            .performClick()
        compose.waitUntil(10_000) { model.preferences.theme == if (toLight) "light" else "dark" }
        compose.onNodeWithTag("page-4").assertExists()
        compose.onNodeWithContentDescription("Apps", useUnmergedTree = true).performClick()
        compose.onNodeWithTag("page-1").assertExists()
        assertEquals(if (toLight) "light" else "dark", model.preferences.theme)
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
        compose.onNode(hasSetTextAction()).assertIsNotFocused()
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
