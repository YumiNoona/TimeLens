package `in`.venusapp.timelens.ui

import androidx.compose.foundation.layout.size
import androidx.compose.material3.MaterialTheme
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.toPixelMap
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.test.*
import androidx.compose.ui.test.junit4.createComposeRule
import androidx.compose.ui.unit.dp
import androidx.test.platform.app.InstrumentationRegistry
import org.junit.Rule
import org.junit.Test

class IconColorTest {
    @get:Rule val compose = createComposeRule()
    @Test
    fun originalStopwatchColorsRemainInLightAndDarkThemes() {
        var dark by mutableStateOf(false)
        val context = InstrumentationRegistry.getInstrumentation().targetContext
        compose.setContent {
            MaterialTheme(colorScheme = monoScheme(dark)) {
                CompositionLocalProvider(LocalDisplay provides DisplayOptions()) {
                    AppIcon(context.packageName, "TimeLens", Modifier.size(128.dp).testTag("brand-icon"))
                }
            }
        }
        fun hasRedHand(): Boolean {
            val pixels = compose.onNodeWithTag("brand-icon").captureToImage().toPixelMap()
            for (y in 0 until pixels.height step 2) for (x in 0 until pixels.width step 2) {
                val color = pixels[x,y]
                if (color.red > .5f && color.red-color.green > .15f) return true
            }
            return false
        }
        compose.waitUntil(10_000) { hasRedHand() }
        compose.runOnIdle { dark = true }
        compose.waitForIdle()
        org.junit.Assert.assertTrue(hasRedHand())
    }
}
