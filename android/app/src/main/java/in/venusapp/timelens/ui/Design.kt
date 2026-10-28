package `in`.venusapp.timelens.ui

import android.graphics.Bitmap
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Apps
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.*
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import java.time.Instant
import java.time.ZoneId
import java.time.format.DateTimeFormatter
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext

internal data class DisplayOptions(
    val seconds: Boolean = false,
    val use24Hour: Boolean = true,
    val compact: Boolean = false,
    val grayIcons: Boolean = false,
)

internal val LocalDisplay = staticCompositionLocalOf { DisplayOptions() }

/** Shared across destinations; bounded to 4 MiB instead of retaining every installed icon. */
private object AppIcons {
    private data class Entry(val bitmap: ImageBitmap, val loadedAt: Long)
    private val cache = object : android.util.LruCache<String, Entry>(4 * 1024 * 1024) {
        override fun sizeOf(key: String, value: Entry) = value.bitmap.width * value.bitmap.height * 4
    }

    fun get(context: android.content.Context, app: String): ImageBitmap? = synchronized(this) {
        val now = android.os.SystemClock.elapsedRealtime()
        cache.get(app)?.takeIf { now - it.loadedAt < 10 * 60_000 }?.let { return it.bitmap }
        runCatching {
            val drawable = context.packageManager.getApplicationIcon(app)
            val bitmap = Bitmap.createBitmap(128, 128, Bitmap.Config.ARGB_8888)
            drawable.setBounds(0, 0, 128, 128)
            drawable.draw(android.graphics.Canvas(bitmap))
            bitmap.asImageBitmap().also { cache.put(app, Entry(it, now)) }
        }.getOrNull()
    }
}

internal fun categoryColor(name: String): Color {
    val shades = listOf(0xFFEEEEEE, 0xFFBDBDBD, 0xFF919191, 0xFF717171, 0xFF555555)
    return Color(shades[(name.hashCode() and Int.MAX_VALUE) % shades.size])
}

@Composable internal fun usage(ms: Long): String = duration(ms, LocalDisplay.current.seconds)

@Composable
internal fun timeLabel(ms: Long): String =
    Instant.ofEpochMilli(ms)
        .atZone(ZoneId.systemDefault())
        .format(
            DateTimeFormatter.ofPattern(if (LocalDisplay.current.use24Hour) "HH:mm" else "h:mm a")
        )

@Composable
internal fun AppIcon(app: String, label: String, modifier: Modifier = Modifier.size(46.dp)) {
    val context = LocalContext.current
    val gray = LocalDisplay.current.grayIcons
    val image by
        produceState<ImageBitmap?>(null, app) {
            value =
                withContext(Dispatchers.IO) {
                    AppIcons.get(context.applicationContext, app)
                }
        }
    Box(
        modifier
            .clip(RoundedCornerShape(13.dp))
            .background(MaterialTheme.colorScheme.surfaceContainerHigh),
        contentAlignment = Alignment.Center,
    ) {
        image?.let {
            Image(
                it,
                contentDescription = null,
                modifier = Modifier.fillMaxSize().padding(3.dp),
                colorFilter =
                    if (gray) ColorFilter.colorMatrix(ColorMatrix().apply { setToSaturation(0f) })
                    else null,
            )
        }
            ?: Icon(
                Icons.Outlined.Apps,
                null,
                tint = MaterialTheme.colorScheme.primary,
                modifier = Modifier.padding(10.dp).fillMaxSize(),
            )
    }
}

@Composable
internal fun Metric(label: String, value: String, caption: String, modifier: Modifier = Modifier) {
    Surface(
        modifier.fillMaxHeight(),
        shape = RoundedCornerShape(20.dp),
        color = MaterialTheme.colorScheme.surfaceContainer,
    ) {
        Column(
            Modifier.padding(if (LocalDisplay.current.compact) 14.dp else 20.dp),
            verticalArrangement = Arrangement.spacedBy(6.dp),
        ) {
            Text(
                label.uppercase(),
                style = MaterialTheme.typography.labelSmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                letterSpacing = 1.sp,
            )
            Text(
                value,
                style = MaterialTheme.typography.headlineSmall,
                fontWeight = FontWeight.Bold,
            )
            Text(
                caption,
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
        }
    }
}

@Composable
internal fun SettingSwitch(
    title: String,
    description: String,
    checked: Boolean,
    onChange: (Boolean) -> Unit,
) {
    val focusManager = androidx.compose.ui.platform.LocalFocusManager.current
    Row(
        Modifier.fillMaxWidth().padding(vertical = 8.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(14.dp),
    ) {
        Column(Modifier.weight(1f)) {
            Text(title, fontWeight = FontWeight.Medium)
            Text(
                description,
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
        }
        Switch(
            checked,
            onCheckedChange = {
                focusManager.clearFocus()
                onChange(it)
            },
            modifier = Modifier.testTag("switch-$title"),
        )
    }
}
