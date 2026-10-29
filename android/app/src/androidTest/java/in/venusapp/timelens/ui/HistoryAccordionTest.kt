package `in`.venusapp.timelens.ui

import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.material3.MaterialTheme
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.semantics.SemanticsActions
import androidx.compose.ui.test.*
import androidx.compose.ui.test.junit4.createComposeRule
import androidx.compose.ui.text.TextLayoutResult
import androidx.compose.ui.unit.Density
import androidx.compose.ui.unit.dp
import `in`.venusapp.timelens.data.*
import `in`.venusapp.timelens.domain.Session
import java.time.LocalDate
import org.junit.Assert.*
import org.junit.Rule
import org.junit.Test

class HistoryAccordionTest {
    @get:Rule val compose = createComposeRule()

    private fun showHistory() {
        val rows =
            listOf(
                Session("fixture.youtube", 1000, 61_000),
                Session("fixture.messages", 61_000, 121_000),
                Session("fixture.youtube", 121_000, 181_000),
            )
        val state =
            UiState(
                day =
                    DayUsage(
                        LocalDate.now(),
                        rows,
                        listOf(
                            AppUsage("fixture.youtube", "YouTube", 120_000, 2, "Entertainment", 0),
                            AppUsage("fixture.messages", "Messages", 60_000, 1, "Communication", 0),
                        ),
                        true,
                    )
            )
        compose.setContent {
            val options = rememberTimelineOptions()
            val content = rememberTimelineContent(state, options)
            MaterialTheme(typography = TimeLensTypography) {
                LazyColumn(
                    Modifier.fillMaxSize(),
                    verticalArrangement = Arrangement.spacedBy(12.dp),
                ) {
                    timelineItems(content, options)
                }
            }
        }
    }

    @Test
    fun appsAreUniqueCollapsedTotalsAndOnlyTheirOwnVisitsExpand() {
        showHistory()
        compose.onAllNodesWithText("YouTube").assertCountEquals(1)
        compose.onNodeWithTag("history-app-fixture.youtube").assert(hasText("2m"))
        compose.onNodeWithTag("history-visit-fixture.youtube-1000").assertDoesNotExist()
        compose.onNodeWithTag("history-app-fixture.youtube").performClick()
        compose.onNodeWithTag("history-visit-fixture.youtube-1000").assertIsDisplayed()
        compose.onNodeWithTag("history-visit-fixture.youtube-121000").assertIsDisplayed()
        compose.onNodeWithTag("history-visit-fixture.messages-61000").assertDoesNotExist()
        compose.onNodeWithContentDescription("Collapse all apps").performClick()
        compose.onNodeWithTag("history-visit-fixture.youtube-1000").assertDoesNotExist()
    }

    @Test
    fun historyFiltersAndSortOperateOnAppsRatherThanDuplicatingVisits() {
        showHistory()
        compose.onNodeWithContentDescription("Filter and sort app history").performClick()
        compose.onNodeWithText("Name").performClick()
        compose.onNodeWithText("Apply").performClick()
        assertTrue(
            compose.onNodeWithTag("history-app-fixture.messages").getUnclippedBoundsInRoot().top <
                compose.onNodeWithTag("history-app-fixture.youtube").getUnclippedBoundsInRoot().top
        )
        compose.onNodeWithContentDescription("Search app history").performClick()
        compose.onNode(hasSetTextAction()).performTextInput("youtube")
        compose.onNodeWithTag("history-app-fixture.messages").assertDoesNotExist()
        compose.onAllNodesWithText("YouTube").assertCountEquals(1)
        compose.onNodeWithContentDescription("Hide history search").performClick()
        compose.onNodeWithText("Search: youtube").assertExists()
    }

    @Test
    fun yearRangeFitsNarrowScreensAtLargeTextAndRemainsSelectable() {
        var days by mutableIntStateOf(7)
        compose.setContent {
            val density = LocalDensity.current
            CompositionLocalProvider(LocalDensity provides Density(density.density, 1.6f)) {
                MaterialTheme(typography = TimeLensTypography) {
                    Box(Modifier.width(280.dp)) { HistoryRangeSelector(days) { days = it } }
                }
            }
        }
        compose.onNodeWithTag("history-range-365").performClick().assertIsSelected()
        assertEquals(365, days)
        listOf("7d", "30d", "90d", "1yr").forEach { label ->
            val layouts = mutableListOf<TextLayoutResult>()
            compose.onNodeWithText(label, useUnmergedTree = true).performSemanticsAction(
                SemanticsActions.GetTextLayoutResult
            ) {
                it(layouts)
            }
            // Variable fonts can report a fractional width a few thousandths above the rounded
            // pixel size. Allow only pixel rounding, not clipped text or a second line.
            assertTrue(
                "$label must fit on one line: "+layouts.joinToString {"size=${it.size}, paragraph=${it.multiParagraph.width}x${it.multiParagraph.height}, line=${it.getLineLeft(0)}..${it.getLineRight(0)}, bottom=${it.getLineBottom(0)}, constraints=${it.layoutInput.constraints}"},
                layouts.isNotEmpty() &&
                    layouts.all {
                        it.lineCount == 1 && !it.didOverflowHeight &&
                            it.getLineLeft(0)>=-1f && it.getLineRight(0)<=it.size.width+1f
                    },
            )
        }
    }
}
