package `in`.venusapp.timelens.ui

import androidx.compose.ui.test.*
import androidx.compose.ui.test.junit4.createAndroidComposeRule
import androidx.lifecycle.ViewModelProvider
import `in`.venusapp.timelens.MainActivity
import `in`.venusapp.timelens.domain.BlockRule
import `in`.venusapp.timelens.domain.PinSecurity
import org.junit.Assert.*
import org.junit.Rule
import org.junit.Test

class UiCleanupTest {
    @get:Rule val compose = createAndroidComposeRule<MainActivity>()

    @Test
    fun dashboardControlsLiveInSettingsAndTheRemovedCardsStayGone() {
        val model = ViewModelProvider(compose.activity)[AppModel::class.java]
        val old = model.preferences.cards
        try {
            compose.waitUntil(15_000) {
                !model.state.value.loading && model.state.value.day != null
            }
            compose.onNodeWithText("Own your attention.").assertExists()
            compose.onNodeWithContentDescription("Customize dashboard").assertDoesNotExist()
            compose.onNodeWithContentDescription("Settings", useUnmergedTree = true).performClick()
            compose.onNodeWithText("Appearance").performClick()
            listOf("Follow system", "Light", "Dark", "Independent. Private. Local.").forEach {
                compose.onNodeWithText(it).assertDoesNotExist()
            }
            compose.onNodeWithText("Dashboard").performScrollTo().performClick()
            val day = model.state.value.day
            compose.onNodeWithTag("dashboard-hourly").performScrollTo().performClick()
            assertEquals("hourly" !in old, "hourly" in model.preferences.cards)
            assertSame(
                "Module choices should not restart activity scanning",
                day,
                model.state.value.day,
            )
        } finally {
            model.preferences.cards = old
        }
    }

    @Test
    fun blockSectionsUseLabelledChoicesAndTheSwitchKeepsItsDisclosure() {
        val model = ViewModelProvider(compose.activity)[AppModel::class.java]
        val oldBlocks = model.preferences.blocksEnabled
        val oldPin = model.preferences.pinHash
        model.preferences.blocksEnabled = false
        model.preferences.pinHash = ""
        try {
            compose.onNodeWithContentDescription("Block", useUnmergedTree = true).performClick()
            compose.onNodeWithContentDescription("App rules").assertIsSelected()
            compose.onNodeWithContentDescription("Website rules").performClick().assertIsSelected()
            compose.onNodeWithText("Web").assertIsDisplayed()
            compose.onNodeWithContentDescription("Block activity").performClick().assertIsSelected()
            compose.onNodeWithTag("page-3").assertExists()
            compose.onNodeWithTag("switch-Blocking").assertIsOff().performClick()
            if (!`in`.venusapp.timelens.platform.AppBlockService.connected)
                compose.onNodeWithText("Enable app blocking?").assertIsDisplayed()
        } finally {
            model.preferences.blocksEnabled = oldBlocks
            model.preferences.pinHash = oldPin
        }
    }

    @Test
    fun ruleSwitchKeepsTheEditorClosedAndRequiresThePin() {
        val model = ViewModelProvider(compose.activity)[AppModel::class.java]
        val oldRules = model.preferences.blockRules
        val oldPin = model.preferences.pinHash
        val oldFailures = model.preferences.pinFailures
        val oldRetry = model.preferences.pinRetryAt
        try {
            model.preferences.blockRules = listOf(BlockRule("web:example.com", "strict"))
            model.preferences.pinHash = PinSecurity.hash("1234")
            model.preferences.pinFailures = 0
            model.preferences.pinRetryAt = 0
            compose.onNodeWithContentDescription("Block", useUnmergedTree = true).performClick()
            compose.onNodeWithContentDescription("Website rules").performClick()
            compose
                .onNodeWithTag("page-3")
                .performScrollToNode(hasContentDescription("Rule for example.com"))
            compose.onNodeWithContentDescription("Rule for example.com").assertIsOn().performClick()
            compose.onNodeWithText("Unlock blocking controls").assertIsDisplayed()
            compose.onNodeWithText("Save rule").assertDoesNotExist()
            assertTrue(model.preferences.blockRules.single().enabled)
            compose.onNodeWithText("Cancel").performClick()
            assertTrue(model.preferences.blockRules.single().enabled)
            val before = model.state.value.revision
            compose.onNodeWithContentDescription("Rule for example.com").performClick()
            compose.onNode(hasSetTextAction()).performTextInput("1234")
            compose.onNode(hasSetTextAction()).performImeAction()
            compose.waitUntil(15_000) {
                !model.preferences.blockRules.single().enabled &&
                    model.state.value.revision > before
            }
            compose.onNodeWithContentDescription("Rule for example.com").assertIsOff()
            compose.onNodeWithText("Save rule").assertDoesNotExist()
        } finally {
            model.preferences.blockRules = oldRules
            model.preferences.pinHash = oldPin
            model.preferences.pinFailures = oldFailures
            model.preferences.pinRetryAt = oldRetry
        }
    }
}
