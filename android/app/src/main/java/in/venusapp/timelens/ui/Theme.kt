package `in`.venusapp.timelens.ui

import androidx.compose.material3.*
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.core.tween
import androidx.compose.runtime.*
import androidx.compose.ui.graphics.lerp
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.ExperimentalTextApi
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.Font
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontVariation
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.sp
import `in`.venusapp.timelens.R

@OptIn(ExperimentalTextApi::class)
internal val SpaceGrotesk =
    FontFamily(
        Font(
            R.font.space_grotesk,
            FontWeight.Normal,
            variationSettings = FontVariation.Settings(FontVariation.weight(400)),
        ),
        Font(
            R.font.space_grotesk,
            FontWeight.Medium,
            variationSettings = FontVariation.Settings(FontVariation.weight(500)),
        ),
        Font(
            R.font.space_grotesk,
            FontWeight.SemiBold,
            variationSettings = FontVariation.Settings(FontVariation.weight(600)),
        ),
        Font(
            R.font.space_grotesk,
            FontWeight.Bold,
            variationSettings = FontVariation.Settings(FontVariation.weight(700)),
        ),
    )

internal val TimeLensTypography =
    Typography(
        displayLarge = type(56, 62, FontWeight.Bold),
        displayMedium = type(44, 50, FontWeight.Bold),
        displaySmall = type(36, 42, FontWeight.Bold),
        headlineLarge = type(32, 38, FontWeight.Bold),
        headlineMedium = type(28, 34, FontWeight.Bold),
        headlineSmall = type(24, 30, FontWeight.Bold),
        titleLarge = type(22, 28, FontWeight.SemiBold),
        titleMedium = type(18, 24, FontWeight.SemiBold),
        titleSmall = type(16, 22, FontWeight.SemiBold),
        bodyLarge = type(16, 24),
        bodyMedium = type(14, 21),
        bodySmall = type(12, 18),
        labelLarge = type(14, 20, FontWeight.SemiBold),
        labelMedium = type(12, 18, FontWeight.Medium),
        labelSmall = type(11, 16, FontWeight.Medium),
    )

private fun type(size: Int, line: Int, weight: FontWeight = FontWeight.Normal) =
    TextStyle(
        fontFamily = SpaceGrotesk,
        fontWeight = weight,
        fontSize = size.sp,
        lineHeight = line.sp,
    )

/** Animate the palette without duplicating the screen, resetting scroll, or taking screenshots. */
@Composable
internal fun animatedMonoScheme(dark: Boolean): ColorScheme {
    val amount by animateFloatAsState(if (dark) 1f else 0f, tween(360), label = "appearance")
    val light = remember { monoScheme(false) }
    val night = remember { monoScheme(true) }
    return light.copy(
        primary = lerp(light.primary, night.primary, amount),
        onPrimary = lerp(light.onPrimary, night.onPrimary, amount),
        primaryContainer = lerp(light.primaryContainer, night.primaryContainer, amount),
        onPrimaryContainer = lerp(light.onPrimaryContainer, night.onPrimaryContainer, amount),
        secondary = lerp(light.secondary, night.secondary, amount),
        onSecondary = lerp(light.onSecondary, night.onSecondary, amount),
        secondaryContainer = lerp(light.secondaryContainer, night.secondaryContainer, amount),
        onSecondaryContainer = lerp(light.onSecondaryContainer, night.onSecondaryContainer, amount),
        tertiary = lerp(light.tertiary, night.tertiary, amount),
        onTertiary = lerp(light.onTertiary, night.onTertiary, amount),
        tertiaryContainer = lerp(light.tertiaryContainer, night.tertiaryContainer, amount),
        onTertiaryContainer = lerp(light.onTertiaryContainer, night.onTertiaryContainer, amount),
        background = lerp(light.background, night.background, amount),
        onBackground = lerp(light.onBackground, night.onBackground, amount),
        surface = lerp(light.surface, night.surface, amount),
        onSurface = lerp(light.onSurface, night.onSurface, amount),
        surfaceVariant = lerp(light.surfaceVariant, night.surfaceVariant, amount),
        onSurfaceVariant = lerp(light.onSurfaceVariant, night.onSurfaceVariant, amount),
        surfaceTint = lerp(light.surfaceTint, night.surfaceTint, amount),
        surfaceContainer = lerp(light.surfaceContainer, night.surfaceContainer, amount),
        surfaceContainerLow = lerp(light.surfaceContainerLow, night.surfaceContainerLow, amount),
        surfaceContainerHigh = lerp(light.surfaceContainerHigh, night.surfaceContainerHigh, amount),
        surfaceContainerHighest = lerp(light.surfaceContainerHighest, night.surfaceContainerHighest, amount),
        surfaceContainerLowest = lerp(light.surfaceContainerLowest, night.surfaceContainerLowest, amount),
        outline = lerp(light.outline, night.outline, amount),
        outlineVariant = lerp(light.outlineVariant, night.outlineVariant, amount),
        error = lerp(light.error, night.error, amount),
        onError = lerp(light.onError, night.onError, amount),
        errorContainer = lerp(light.errorContainer, night.errorContainer, amount),
        onErrorContainer = lerp(light.onErrorContainer, night.onErrorContainer, amount),
        inverseSurface = lerp(light.inverseSurface, night.inverseSurface, amount),
        inverseOnSurface = lerp(light.inverseOnSurface, night.inverseOnSurface, amount),
        inversePrimary = lerp(light.inversePrimary, night.inversePrimary, amount),
        scrim = lerp(light.scrim, night.scrim, amount),
    )
}

internal fun monoScheme(dark: Boolean): ColorScheme =
    if (dark)
        darkColorScheme(
            primary = Color(0xFFF2F2F2),
            onPrimary = Color(0xFF101010),
            primaryContainer = Color(0xFF282828),
            onPrimaryContainer = Color.White,
            secondary = Color(0xFFCCCCCC),
            secondaryContainer = Color(0xFF303030),
            onSecondaryContainer = Color.White,
            tertiary = Color(0xFFBBBBBB),
            tertiaryContainer = Color(0xFF252525),
            onTertiaryContainer = Color.White,
            background = Color(0xFF0B0B0B),
            onBackground = Color(0xFFF4F4F4),
            surface = Color(0xFF101010),
            onSurface = Color(0xFFF4F4F4),
            onSurfaceVariant = Color(0xFFB5B5B5),
            surfaceVariant = Color(0xFF252525),
            surfaceContainer = Color(0xFF191919),
            surfaceContainerLow = Color(0xFF141414),
            surfaceContainerHigh = Color(0xFF222222),
            surfaceContainerHighest = Color(0xFF303030),
            surfaceContainerLowest = Color(0xFF080808),
            outline = Color(0xFF707070),
            outlineVariant = Color(0xFF363636),
            error = Color(0xFFE0E0E0),
            onError = Color.Black,
            errorContainer = Color(0xFF353535),
            onErrorContainer = Color.White,
            inverseSurface = Color(0xFFEEEEEE),
            inverseOnSurface = Color(0xFF161616),
            inversePrimary = Color.Black,
        )
    else
        lightColorScheme(
            primary = Color(0xFF151515),
            onPrimary = Color.White,
            primaryContainer = Color(0xFFE6E6E6),
            onPrimaryContainer = Color.Black,
            secondary = Color(0xFF454545),
            secondaryContainer = Color(0xFFE6E6E6),
            onSecondaryContainer = Color.Black,
            tertiary = Color(0xFF555555),
            tertiaryContainer = Color(0xFFEEEEEE),
            onTertiaryContainer = Color.Black,
            background = Color(0xFFF8F8F8),
            onBackground = Color(0xFF141414),
            surface = Color.White,
            onSurface = Color(0xFF141414),
            onSurfaceVariant = Color(0xFF555555),
            surfaceVariant = Color(0xFFE9E9E9),
            surfaceContainer = Color(0xFFF0F0F0),
            surfaceContainerLow = Color(0xFFF5F5F5),
            surfaceContainerHigh = Color(0xFFE7E7E7),
            surfaceContainerHighest = Color(0xFFDDDDDD),
            surfaceContainerLowest = Color.White,
            outline = Color(0xFF777777),
            outlineVariant = Color(0xFFCCCCCC),
            error = Color(0xFF303030),
            onError = Color.White,
            errorContainer = Color(0xFFE1E1E1),
            onErrorContainer = Color.Black,
            inverseSurface = Color(0xFF222222),
            inverseOnSurface = Color.White,
            inversePrimary = Color.White,
        )
