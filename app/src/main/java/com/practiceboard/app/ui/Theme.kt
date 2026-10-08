package com.practiceboard.app.ui

import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.ui.graphics.Color
import com.practiceboard.app.data.Sphere

private val DarkColors = darkColorScheme(
    primary = Color(0xFF8BD3A0),
    onPrimary = Color(0xFF0B2614),
    primaryContainer = Color(0xFF2F5A3E),
    onPrimaryContainer = Color(0xFFCDEFD8),
    secondary = Color(0xFFFFC27A),
    tertiary = Color(0xFFFF8FB8),
    background = Color(0xFF121714),
    onBackground = Color(0xFFE2E8E3),
    surface = Color(0xFF121714),
    onSurface = Color(0xFFE2E8E3),
    surfaceVariant = Color(0xFF26302A),
    onSurfaceVariant = Color(0xFFBFC9C1),
)

private val LightColors = lightColorScheme(
    primary = Color(0xFF2E7D4F),
    onPrimary = Color.White,
    primaryContainer = Color(0xFFC8EBD3),
    onPrimaryContainer = Color(0xFF0B2614),
    secondary = Color(0xFFB86E12),
    tertiary = Color(0xFFC2185B),
    background = Color(0xFFF4F7F3),
    onBackground = Color(0xFF1A1F1B),
    surface = Color(0xFFF4F7F3),
    onSurface = Color(0xFF1A1F1B),
    surfaceVariant = Color(0xFFDDE5DE),
    onSurfaceVariant = Color(0xFF414943),
)

/** Цвета «школьной доски» — одинаковые в приложении и в виджете. */
object BoardColors {
    val wood = Color(0xFF8D6E4A)
    val board = Color(0xFF2B3A31)
    val item = Color(0xFF3A4B40)
    val itemDone = Color(0xFF2F5A3E)
    val accent = Color(0xFF8BD3A0)
    val text = Color.White
    val textDim = Color(0xB3FFFFFF)
}

fun sphereColorOnBoard(sphere: Sphere): Color = when (sphere) {
    Sphere.CODE -> Color(0xFF7FD3FF)
    Sphere.MODEL3D -> Color(0xFFFFC27A)
    Sphere.ART2D -> Color(0xFFFF8FB8)
}

@Composable
fun sphereColor(sphere: Sphere): Color {
    val dark = isSystemInDarkTheme()
    return when (sphere) {
        Sphere.CODE -> if (dark) Color(0xFF7FD3FF) else Color(0xFF1B78B5)
        Sphere.MODEL3D -> if (dark) Color(0xFFFFC27A) else Color(0xFFB8650C)
        Sphere.ART2D -> if (dark) Color(0xFFFF8FB8) else Color(0xFFC2185B)
    }
}

@Composable
fun PracticeTheme(content: @Composable () -> Unit) {
    MaterialTheme(
        colorScheme = if (isSystemInDarkTheme()) DarkColors else LightColors,
        content = content,
    )
}
