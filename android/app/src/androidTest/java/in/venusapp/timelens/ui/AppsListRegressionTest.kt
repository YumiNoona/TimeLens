package `in`.venusapp.timelens.ui

import android.app.Application
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.material3.MaterialTheme
import androidx.compose.ui.Modifier
import androidx.compose.ui.test.*
import androidx.compose.ui.test.junit4.createComposeRule
import androidx.lifecycle.ViewModelStore
import androidx.test.platform.app.InstrumentationRegistry
import `in`.venusapp.timelens.data.AppUsage
import `in`.venusapp.timelens.data.DayUsage
import java.time.LocalDate
import org.junit.Assert.assertTrue
import org.junit.Rule
import org.junit.Test

class AppsListRegressionTest {
    @get:Rule val compose = createComposeRule()

    @Test
    fun largeInventoryOnlyComposesVisibleRowsAndCanScrollToTheEnd() {
        val app = InstrumentationRegistry.getInstrumentation().targetContext.applicationContext as Application
        val model = AppModel(app)
        val models = ViewModelStore().apply { put("test", model) }
        val rows = (0 until 1000).map {
            AppUsage("synthetic.app.$it", "Synthetic app $it", 1000L - it, 1, "Other", 0)
        }
        val state = UiState(day = DayUsage(LocalDate.now(), emptyList(), rows, true))
        try {
            compose.setContent {
                MaterialTheme { AppsScreen(state, model, Modifier.fillMaxSize()) }
            }
            compose.onNodeWithText("Synthetic app 0").assertIsDisplayed()
            compose.onNodeWithText("Synthetic app 999").assertDoesNotExist()
            assertTrue(compose.onAllNodes(hasText("Synthetic app", substring = true)).fetchSemanticsNodes().size < 20)
            compose.onNode(hasScrollToIndexAction()).performScrollToIndex(1000)
            compose.onNodeWithText("Synthetic app 999").assertIsDisplayed()
            compose.onNodeWithText("Synthetic app 0").assertDoesNotExist()
        } finally {
            compose.runOnIdle { models.clear() }
        }
    }
}
