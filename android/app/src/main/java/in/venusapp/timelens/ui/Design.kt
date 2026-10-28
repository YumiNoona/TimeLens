package `in`.venusapp.timelens.ui

import android.graphics.Bitmap
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.*
import androidx.compose.ui.platform.LocalContext
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
)

internal val LocalDisplay = staticCompositionLocalOf { DisplayOptions() }
internal val AccentColors =
    linkedMapOf(
        "acid" to Color(0xFFC8E86A),
        "arctic" to Color(0xFF81D4D1),
        "lavender" to Color(0xFFC5AFFB),
        "copper" to Color(0xFFFFBB86),
        "ocean" to Color(0xFF8AC7FF),
        "rose" to Color(0xFFF7A7C5),
    )

internal fun categoryColor(name: String): Color =
    when (name) {
        "Work" -> Color(0xFFBADD79)
        "Education" -> Color(0xFF7DCFCB)
        "Communication" -> Color(0xFF91BBF4)
        "Social" -> Color(0xFFF0A0BA)
        "Entertainment" -> Color(0xFFC5A4ED)
        "Gaming" -> Color(0xFFE5B27E)
        "Utilities" -> Color(0xFF9CB5A0)
        else -> Color(0xFF939A8A)
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
    val image by
        produceState<ImageBitmap?>(null, app) {
            value =
                withContext(Dispatchers.IO) {
                    runCatching {
                        val drawable = context.packageManager.getApplicationIcon(app)
                        val bitmap = Bitmap.createBitmap(128, 128, Bitmap.Config.ARGB_8888)
                        drawable.setBounds(0, 0, 128, 128)
                        drawable.draw(android.graphics.Canvas(bitmap))
                        bitmap.asImageBitmap()
                    }
                        .getOrNull()
                }
        }
    Box(
        modifier
            .clip(RoundedCornerShape(13.dp))
            .background(MaterialTheme.colorScheme.surfaceContainerHigh),
        contentAlignment = Alignment.Center,
    ) {
        image?.let {
            Image(it, contentDescription = null, modifier = Modifier.fillMaxSize().padding(3.dp))
        }
            ?: Text(
                label.take(1).uppercase(),
                fontWeight = FontWeight.Bold,
                color = MaterialTheme.colorScheme.primary,
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
        Switch(checked, onCheckedChange = onChange)
    }
}
