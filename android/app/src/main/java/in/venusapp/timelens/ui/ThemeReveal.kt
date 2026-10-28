package `in`.venusapp.timelens.ui

import androidx.compose.animation.core.Animatable
import androidx.compose.animation.core.FastOutSlowInEasing
import androidx.compose.animation.core.tween
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.drawWithContent
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Rect
import androidx.compose.ui.graphics.ClipOp
import androidx.compose.ui.graphics.ImageBitmap
import androidx.compose.ui.graphics.Path
import androidx.compose.ui.graphics.drawscope.clipPath
import androidx.compose.ui.graphics.layer.drawLayer
import androidx.compose.ui.graphics.rememberGraphicsLayer
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.launch
import kotlin.math.hypot

/** One short-lived old frame; no disk capture or whole-screen recomposition on every tick. */
@Composable
internal fun ThemeReveal(onChange: (String) -> Unit, content: @Composable ((String) -> Unit) -> Unit) {
    val layer = rememberGraphicsLayer()
    val scope = rememberCoroutineScope()
    val progress = remember { Animatable(1f) }
    var oldFrame by remember { mutableStateOf<ImageBitmap?>(null) }
    var switching by remember { mutableStateOf(false) }
    val change: (String) -> Unit = { theme ->
        if (!switching) {
            switching = true
            scope.launch {
                try {
                    val frame = layer.toImageBitmap()
                    progress.snapTo(0f)
                    oldFrame = frame
                    onChange(theme)
                    progress.animateTo(1f, tween(380, easing = FastOutSlowInEasing))
                } catch (cancelled: CancellationException) {
                    throw cancelled
                } catch (error: Exception) {
                    android.util.Log.w("TimeLensTheme", "Theme capture unavailable; applying appearance without an overlay", error)
                    onChange(theme)
                } finally {
                    oldFrame = null
                    switching = false
                }
            }
        }
    }
    Box(Modifier.fillMaxSize()) {
        Box(Modifier.fillMaxSize().drawWithContent {
            layer.record { this@drawWithContent.drawContent() }
            drawLayer(layer)
        }) { content(change) }
        oldFrame?.let { frame ->
            Canvas(Modifier.matchParentSize()) {
                val origin = Offset(size.width - 28f * density, 52f * density)
                val radius = hypot(size.width, size.height) * progress.value
                val reveal = Path().apply {
                    addOval(Rect(origin.x-radius, origin.y-radius, origin.x+radius, origin.y+radius))
                }
                clipPath(reveal, ClipOp.Difference) { drawImage(frame) }
            }
        }
    }
}
