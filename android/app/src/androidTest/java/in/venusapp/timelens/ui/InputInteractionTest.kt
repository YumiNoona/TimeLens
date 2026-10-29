package `in`.venusapp.timelens.ui

import androidx.compose.material3.MaterialTheme
import androidx.compose.runtime.*
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.test.*
import androidx.compose.ui.test.junit4.createComposeRule
import androidx.compose.ui.text.input.ImeAction
import androidx.test.platform.app.InstrumentationRegistry
import `in`.venusapp.timelens.data.Preferences
import `in`.venusapp.timelens.domain.PinSecurity
import java.util.concurrent.atomic.AtomicInteger
import org.junit.Assert.*
import org.junit.Rule
import org.junit.Test

class InputInteractionTest {
    @get:Rule val compose = createComposeRule()

    @Test
    fun labelTapChangesSwitchExactlyOnce() {
        var enabled by mutableStateOf(false)
        var calls = 0
        compose.setContent {
            MaterialTheme {
                SettingSwitch("Blocking", "Protect selected apps", enabled) {
                    calls++
                    enabled = it
                }
            }
        }
        compose.onNodeWithText("Blocking").performClick()
        compose.onNodeWithTag("switch-Blocking").assertIsOn()
        assertEquals(1, calls)
        compose.onNodeWithTag("switch-Blocking").performTouchInput {
            click(centerRight - Offset(1f, 0f))
        }
        compose.onNodeWithTag("switch-Blocking").assertIsOff()
        assertEquals(2, calls)
    }

    @Test
    fun searchKeyDismissesTypingAndKeepsTheQuery() {
        var query by mutableStateOf("")
        var requestFocus by mutableStateOf(true)
        compose.setContent {
            MaterialTheme {
                SearchInput(
                    query,
                    { query = it },
                    "Find apps",
                    requestFocus = requestFocus,
                    onFocusRequested = { requestFocus = false },
                )
            }
        }
        compose
            .onNode(hasSetTextAction())
            .assertIsFocused()
            .assert(hasImeAction(ImeAction.Search))
            .performTextInput("YouTube")
        compose.onNode(hasSetTextAction()).performImeAction()
        compose.onNode(hasSetTextAction()).assertIsNotFocused()
        assertEquals("YouTube", query)
        compose.onNodeWithContentDescription("Clear search").performClick()
        assertEquals("", query)
    }

    @Test
    fun pinDoneUsesTheSameValidationAndRetryLimitAsUnlock() {
        val prefs = Preferences(InstrumentationRegistry.getInstrumentation().targetContext)
        val originalHash = prefs.pinHash
        val originalFailures = prefs.pinFailures
        val originalRetry = prefs.pinRetryAt
        val accepted = AtomicInteger()
        try {
            prefs.pinHash = PinSecurity.hash("1234")
            prefs.pinFailures = 0
            prefs.pinRetryAt = 0
            compose.setContent {
                MaterialTheme { PinChallenge(prefs, { accepted.incrementAndGet() }, {}) }
            }
            compose.onNode(hasSetTextAction()).assertIsFocused().performTextInput("1235")
            compose.onNode(hasSetTextAction()).performImeAction()
            compose.waitUntil(15_000) { prefs.pinFailures == 1 }
            compose.onNodeWithText("Incorrect PIN").assertExists()
            assertEquals(0, accepted.get())
            prefs.pinRetryAt = System.currentTimeMillis() + 60_000
            compose.onNode(hasSetTextAction()).performTextReplacement("1234")
            compose.onNode(hasSetTextAction()).performImeAction()
            compose.onNodeWithText("Please wait a minute before trying again.").assertExists()
            assertEquals(0, accepted.get())
            prefs.pinRetryAt = 0
            compose.onNode(hasSetTextAction()).performImeAction()
            compose.waitUntil(15_000) { accepted.get() == 1 }
            assertEquals(0, prefs.pinFailures)
        } finally {
            prefs.pinHash = originalHash
            prefs.pinFailures = originalFailures
            prefs.pinRetryAt = originalRetry
        }
    }
}
