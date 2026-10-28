package `in`.venusapp.timelens.ui

import androidx.compose.material3.*
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
