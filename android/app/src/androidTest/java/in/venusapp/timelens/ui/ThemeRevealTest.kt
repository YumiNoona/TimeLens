package `in`.venusapp.timelens.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.*
import androidx.compose.material3.Button
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.toPixelMap
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.test.*
import androidx.compose.ui.test.junit4.createComposeRule
import org.junit.Assert.*
import org.junit.Rule
import org.junit.Test

class ThemeRevealTest {
    @get:Rule val compose = createComposeRule()

    @Test
    fun revealHasAnIntermediateFrameInsteadOfSnappingTheWholeScreen() {
        var light by mutableStateOf(false)
        var density = 1f
        compose.setContent {
            density = LocalDensity.current.density
            ThemeReveal(onChange = { light = it == "light" }) { change ->
                MaterialTheme {
                    Box(
                        Modifier.fillMaxSize().background(if (light) Color.White else Color.Black)
                    ) {
                        Button(
                            onClick = { change("light") },
                            modifier = Modifier.align(Alignment.BottomCenter),
                        ) {
                            Text("Change theme")
                        }
                    }
                }
            }
        }
        compose.waitForIdle()
        val initialImage = compose.onRoot().captureToImage()
        val initial = initialImage.toPixelMap()
        assertTrue(
            "Initial old-theme background must be black",
            initial[(initial.width * .1f).toInt(), (initial.height * .7f).toInt()].red < .1f,
        )
        compose.mainClock.autoAdvance = false
        compose.onNodeWithText("Change theme").performClick()
        compose.mainClock.advanceTimeBy(32)
        compose.waitUntil(10_000) { light }
        compose.mainClock.advanceTimeBy(100)
        val middle = compose.onRoot().captureToImage().toPixelMap()
        val x = (middle.width - 28 * density).toInt().coerceIn(0, middle.width - 1)
        val y = (52 * density).toInt().coerceIn(0, middle.height - 1)
        assertTrue("The new theme must be visible near the toggle", middle[x, y].red > .9f)
        assertTrue(
            "The old theme must still exist outside the reveal",
            middle[(middle.width * .1f).toInt(), (middle.height * .7f).toInt()].red < .1f,
        )
        compose.mainClock.advanceTimeBy(500)
        compose.waitForIdle()
        val finished = compose.onRoot().captureToImage().toPixelMap()
        assertTrue(
            finished[(finished.width * .1f).toInt(), (finished.height * .7f).toInt()].red > .9f
        )
        compose.mainClock.autoAdvance = true
    }

    @Test
    fun ordinaryContentChangesNeverCaptureAFullScreenFrame() {
        var changes by mutableIntStateOf(0)
        var captures = 0
        var light by mutableStateOf(false)
        compose.setContent {
            ThemeReveal(onChange = { light = it == "light" }, onCapture = { captures++ }) { theme ->
                Column {
                    Button(onClick = { changes++ }) { Text("Next page") }
                    Text("Page $changes")
                    Button(onClick = { theme("light") }) { Text("Theme") }
                }
            }
        }
        repeat(4) { compose.onNodeWithText("Next page").performClick() }
        compose.waitForIdle()
        assertEquals(0, captures)
        compose.onNodeWithText("Theme").performClick()
        compose.waitUntil(10_000) { light && captures == 1 }
    }
}
