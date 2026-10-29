package `in`.venusapp.timelens.ui

import android.app.Application
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.material3.MaterialTheme
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.test.*
import androidx.compose.ui.test.junit4.createComposeRule
import androidx.lifecycle.ViewModelStore
import androidx.test.platform.app.InstrumentationRegistry
import `in`.venusapp.timelens.data.*
import `in`.venusapp.timelens.domain.*
import java.time.LocalDate
import org.junit.Assert.*
import org.junit.Rule
import org.junit.Test

class MobileControlsTest {
    @get:Rule val compose = createComposeRule()

    private fun model() =
        AppModel(
            InstrumentationRegistry.getInstrumentation().targetContext.applicationContext
                as Application
        )

    @Test
    fun favoritesPersistAndTheFavoriteFilterUpdatesWithoutAnActivityScan() {
        val model = model()
        val models = ViewModelStore().apply { put("test", model) }
        val old = model.preferences.favorites
        model.preferences.favorites = emptySet()
        val entered = java.util.concurrent.CountDownLatch(1)
        val release = java.util.concurrent.CountDownLatch(1)
        val fixture =
            UiState(
                day =
                    DayUsage(
                        LocalDate.now(),
                        emptyList(),
                        listOf(
                            AppUsage("fixture.favorite", "Priority app", 1000, 1, "Work", 0),
                            AppUsage("fixture.other", "Other app", 500, 1, "Other", 0),
                        ),
                        true,
                    )
            )
        try {
            compose.setContent {
                val live by model.state.collectAsState()
                val favorites = remember(live.revision) { model.preferences.favorites }
                MaterialTheme {
                    CompositionLocalProvider(LocalFavorites provides favorites) {
                        AppsScreen(
                            fixture.copy(revision = live.revision),
                            model,
                            Modifier.fillMaxSize(),
                        )
                    }
                }
            }
            compose.runOnUiThread {
                model.edit(refreshActivity = false) {
                    entered.countDown()
                    release.await(10, java.util.concurrent.TimeUnit.SECONDS)
                }
            }
            assertTrue(entered.await(10, java.util.concurrent.TimeUnit.SECONDS))
            compose.onNodeWithContentDescription("Favorite Priority app").performClick()
            compose.waitUntil(10_000) {
                model.preferences.favorites.contains("fixture.favorite") &&
                    model.state.value.revision > 0
            }
            assertNull(model.state.value.day)
            release.countDown()
            compose.onNodeWithContentDescription("Show favorite apps").performClick()
            compose.onNodeWithText("Priority app").assertIsDisplayed()
            compose.onNodeWithText("Other app").assertDoesNotExist()
            compose.onNodeWithContentDescription("Unfavorite Priority app").performClick()
            compose.waitUntil(10_000) { !model.preferences.favorites.contains("fixture.favorite") }
            compose.onNodeWithText("No apps match").assertExists()
        } finally {
            release.countDown()
            model.preferences.favorites = old
            compose.runOnIdle { models.clear() }
        }
    }

    @Test
    fun bulkPresetOnlyChangesChosenAppsAndPreservesTheChosenMode() {
        var saved: List<BlockRule>? = null
        val state =
            UiState(
                installed =
                    listOf(
                        InstalledApp("fixture.social", "Social app"),
                        InstalledApp("fixture.work", "Work app"),
                    ),
                appCategories = mapOf("fixture.social" to "Social", "fixture.work" to "Work"),
            )
        compose.setContent {
            MaterialTheme { BulkRulesDialog(state, emptySet(), emptySet(), {}, { saved = it }) }
        }
        compose.onNodeWithText("Distractions").performClick()
        compose.onNodeWithText("Strict").performClick()
        compose.onNodeWithText("Save 1 rule").performClick()
        assertEquals(listOf("fixture.social"), saved?.map { it.app })
        assertEquals("strict", saved?.single()?.mode)
    }

    @Test
    fun aPopulatedYearKeepsAppVisitsLazyWithoutTheRemovedCalendar() {
        val model = model()
        val models = ViewModelStore().apply { put("test", model) }
        val date = LocalDate.now()
        val rows =
            (0 until 600).map { Session("fixture.app.${it%40}", it * 1000L + 1, it * 1000L + 900) }
        val apps =
            (0 until 40).map {
                AppUsage("fixture.app.$it", "History app $it", 1000, 15, "Other", 0)
            }
        val fixture =
            UiState(
                date = date,
                day = DayUsage(date, rows, apps, true),
                historyRange = 365,
                report =
                    HistoryReport(
                        date,
                        365,
                        (0 until 365).map { date.minusDays(364L - it) to 1000L },
                        apps,
                        365_000,
                    ),
            )
        try {
            compose.setContent {
                MaterialTheme {
                    HistoryScreen(fixture, model, Modifier.fillMaxSize().testTag("history-fixture"))
                }
            }
            compose.onNodeWithText("Activity calendar").assertDoesNotExist()
            assertTrue(
                compose
                    .onAllNodes(hasContentDescription("$date:", substring = true))
                    .fetchSemanticsNodes()
                    .isEmpty()
            )
            compose.onNodeWithTag("history-fixture").performScrollToNode(hasText("App history"))
            compose.onNodeWithText("App history").assertIsDisplayed()
            compose.onNodeWithTag("history-visit-fixture.app.0-1").assertDoesNotExist()
            assertTrue(
                compose
                    .onAllNodes(hasText("History app", substring = true))
                    .fetchSemanticsNodes()
                    .size < 20
            )
        } finally {
            compose.runOnIdle { models.clear() }
        }
    }
}
